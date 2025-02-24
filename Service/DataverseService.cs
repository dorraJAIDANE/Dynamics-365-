using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using System.Diagnostics;
using System.Text;


public class DataverseService
{
    private readonly ServiceClient _serviceClient;

    public DataverseService(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Dataverse");
        _serviceClient = new ServiceClient(connectionString);
    }
    public List<string> GetAllSolutions()
    {
        try
        {
            // Define the query to retrieve all solutions
            var query = new QueryExpression("solution")
            {
                ColumnSet = new ColumnSet("uniquename", "friendlyname")
            };

            // Execute the query
            var solutions = _serviceClient.RetrieveMultiple(query).Entities;

            // Extract the unique names and friendly names of the solutions
            var solutionNames = solutions.Select(s =>
                s.GetAttributeValue<string>("friendlyname")

            ).ToList();

            return solutionNames;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error retrieving solutions: {ex.Message}");
            return new List<string>();
        }
    }
    /// <summary>
    /// Récupère toutes les entités disponibles dans Dataverse.
    /// </summary>

    public List<string> GetAllEntities(string solutionUniqueName)
    {
        try
        {
            // Récupérer l'ID de la solution via son nom unique
            var query = new QueryExpression("solution")
            {
                ColumnSet = new ColumnSet("solutionid"),
                Criteria = new FilterExpression
                {
                    Conditions =
                {
                    new ConditionExpression("friendlyname", ConditionOperator.Equal, solutionUniqueName)
                }
                }
            };

            var solution = _serviceClient.RetrieveMultiple(query).Entities.FirstOrDefault();
            //Debug.WriteLine($"Solution Unique Name: {solutionUniqueName}");
            if (solution == null)
            {
                throw new Exception($"Solution '{solutionUniqueName}' non trouvée.");
            }

            // Récupérer les composants de la solution en interrogeant l'entité "solutioncomponent"
            var queryComponents = new QueryExpression("solutioncomponent")
            {
                ColumnSet = new ColumnSet("objectid", "componenttype"),
                Criteria = new FilterExpression
                {
                    Conditions =
                {
                    new ConditionExpression("solutionid", ConditionOperator.Equal, solution.Id)
                }
                }
            };

            var solutionComponents = _serviceClient.RetrieveMultiple(queryComponents).Entities;
            Debug.WriteLine($"solutionComponents: {solutionComponents}");

            // Extraire les IDs des entités appartenant à la solution
            var entityIds = solutionComponents
                 .Where(c => c.GetAttributeValue<OptionSetValue>("componenttype")?.Value == 1) // 1 = Entity
                 .Select(c => c.GetAttributeValue<Guid?>("objectid"))
                 .Where(id => id.HasValue)
                 .Select(id => id.Value)
                 .ToList();
            // Récupérer les métadonnées de toutes les entités
            var entityMetadataRequest = new RetrieveAllEntitiesRequest
            {
                EntityFilters = EntityFilters.Entity,
                RetrieveAsIfPublished = true
            };

            var entityMetadataResponse = (RetrieveAllEntitiesResponse)_serviceClient.Execute(entityMetadataRequest);

            // Liste des entités standards
            var standardEntityNames = new HashSet<string> { "contact", "lead", "opportunity", "account" };

            // Filtrer les entités appartenant à la solution
            var entitiesInSolution = entityMetadataResponse.EntityMetadata
                .Where(e => entityIds.Contains((Guid)e.MetadataId))
                .Select(e => new
                {
                    //Name = e.DisplayName?.UserLocalizedLabel?.Label ?? e.LogicalName,
                    Name = e.LogicalName,
                    IsStandard = standardEntityNames.Contains(e.LogicalName)
                })
                .ToList();

            // Séparer entités standards et personnalisées
            var standardEntities = entitiesInSolution.Where(e => e.IsStandard).Select(e => e.Name).ToList();
            var customEntities = entitiesInSolution.Where(e => !e.IsStandard).Select(e => e.Name).ToList();

            // Combiner les résultats
            var combinedEntities = standardEntities.Union(customEntities).ToList();
            Debug.WriteLine($"combinedEntities: {combinedEntities}");

            return combinedEntities;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Erreur lors de la récupération des entités pour la solution '{solutionUniqueName}': {ex.Message}");
            return new List<string>();
        }
    }


    /// <summary>
    /// Récupère toutes les données d'une entité spécifique.
    /// </summary>
    public List<Dictionary<string, object>> GetEntityAttributesWithOptions(string entityName)
    {
        try
        {
            var entityRequest = new RetrieveEntityRequest
            {
                LogicalName = entityName,
                EntityFilters = EntityFilters.Attributes | EntityFilters.Relationships,
                RetrieveAsIfPublished = true
            };

            var entityResponse = (RetrieveEntityResponse)_serviceClient.Execute(entityRequest);

            if (entityResponse?.EntityMetadata == null)
            {
                Debug.WriteLine($"Aucun attribut trouvé pour l'entité : {entityName}");
                return new List<Dictionary<string, object>>();
            }

            // 🔥 Récupérer la clé primaire correcte de l'entité
            var primaryKey = entityResponse.EntityMetadata.PrimaryIdAttribute;

            var attributes = entityResponse.EntityMetadata.Attributes
                .Where(attr => attr.IsValidForRead == true)
                .Where(attr => attr.IsValidForForm == true)
                .Where(attr => !new[] { "createdon", "statuscode", "statecode", "modifiedon" }
                    .Contains(attr.LogicalName.ToLower()))
                .Where(attr => attr.DisplayName != null && attr.DisplayName.UserLocalizedLabel != null)
                .Where(attr => !string.IsNullOrEmpty(attr.DisplayName.UserLocalizedLabel.Label))
                .Where(attr => !HasDefaultValue(attr))
                .Select(attr => new Dictionary<string, object>
                {
                { "LogicalName", attr.LogicalName },
                { "DisplayName", attr.DisplayName?.UserLocalizedLabel?.Label ?? attr.LogicalName },
                { "AttributeType", attr.AttributeType.Value.ToString() },
                { "Options", attr.AttributeType == AttributeTypeCode.Picklist ? GetOptionSetValues(entityName, attr.LogicalName) : null }
                })
                .ToList();

            // 🔥 Récupération des entités liées (Lookup)
            foreach (var relationship in entityResponse.EntityMetadata.ManyToOneRelationships)
            {
                var relatedEntityName = relationship.ReferencedEntity;
                var relatedEntityIdAttribute = relationship.ReferencedAttribute;

                if (!string.IsNullOrEmpty(relatedEntityName) && !string.IsNullOrEmpty(relatedEntityIdAttribute))
                {
                    var relatedEntities = GetRelatedEntities(entityName, relatedEntityName, relatedEntityIdAttribute, primaryKey);
                    if (relatedEntities.Any())
                    {
                        attributes.Add(new Dictionary<string, object>
                    {
                        { "LogicalName", relatedEntityName },
                        { "DisplayName", relatedEntityName },
                        { "AttributeType", "RelatedEntity" },
                        { "Options", relatedEntities }
                    });

                        // 🔥 Récupérer les options set pour les entités liées
                        var relatedEntityOptions = GetOptionSetValues(relatedEntityName, relatedEntityIdAttribute);
                        if (relatedEntityOptions.Any())
                        {
                            attributes.Add(new Dictionary<string, object>
                        {
                            { "LogicalName", relatedEntityName },
                            { "DisplayName", relatedEntityName + " Options" },
                            { "AttributeType", "Picklist" },
                            { "Options", relatedEntityOptions }
                        });
                        }
                    }
                }
            }

            return attributes;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Erreur lors de la récupération des attributs de {entityName}: {ex.Message}");
            return new List<Dictionary<string, object>>();
        }
    }

    public List<Dictionary<string, object>> GetRelatedEntities(string entityName, string relatedEntityName, string relatedEntityIdAttribute, string primaryKey)
    {
        try
        {
            // 🔥 Vérifie si la clé primaire de l'entité principale est bien définie
            if (string.IsNullOrEmpty(primaryKey))
            {
                Debug.WriteLine($"Clé primaire non trouvée pour l'entité {entityName}");
                return new List<Dictionary<string, object>>();
            }

            var query = new QueryExpression(relatedEntityName)
            {
                ColumnSet = new ColumnSet(relatedEntityIdAttribute, "fullname")
            };

            var entities = _serviceClient.RetrieveMultiple(query).Entities;

            var result = new List<Dictionary<string, object>>();

            foreach (var entity in entities)
            {
                var entityData = new Dictionary<string, object>
            {
                { "Id", entity.Id.ToString() }, // Assurez-vous que c'est bien l'ID de l'entité
                { "Name", entity.Contains("fullname") ? entity["fullname"] : "Sans nom" } // Adapte selon l'entité
            };

                result.Add(entityData);
            }

            return result;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Erreur lors de la récupération des entités liées pour {entityName}: {ex.Message}");
            return new List<Dictionary<string, object>>();
        }
    }

    public Dictionary<int, string> GetOptionSetValues(string entityName, string attributeName)
    {
        var attributeRequest = new RetrieveAttributeRequest
        {
            EntityLogicalName = entityName,
            LogicalName = attributeName,
            RetrieveAsIfPublished = true
        };

        var attributeResponse = (RetrieveAttributeResponse)_serviceClient.Execute(attributeRequest);
        var attributeMetadata = attributeResponse.AttributeMetadata;

        if (attributeMetadata is PicklistAttributeMetadata picklistMetadata)
        {
            return picklistMetadata.OptionSet.Options.ToDictionary(opt => opt.Value.Value, opt => opt.Label.UserLocalizedLabel.Label);
        }

        return new Dictionary<int, string>();
    }

   


    // Méthode pour vérifier si un attribut a une valeur par défaut (exemple avec Boolean ou Picklist)
    private bool HasDefaultValue(AttributeMetadata attribute)
    {
        // Si l'attribut est de type Boolean, on vérifie sa valeur par défaut
        if (attribute is BooleanAttributeMetadata booleanAttribute)
        {
            return booleanAttribute.DefaultValue.HasValue; // Exclure si une valeur par défaut est définie
        }

        // Si l'attribut est de type Picklist, on vérifie les valeurs par défaut dans OptionSet
        if (attribute is PicklistAttributeMetadata picklistAttribute)
        {
           // var defaultValue = picklistAttribute.OptionSet.DefaultOption?.Value;
           // return defaultValue.HasValue; // Exclure si une valeur par défaut est définie
        }

        return false; // Ne pas filtrer d'autres types d'attributs
    }

   
    public Guid CreateRecord(string entityName, List<string> selectedFields)
    {
        try
        {
            // Créer une instance de l'entité
            Entity record = new Entity(entityName);

            // Ajouter les champs sélectionnés avec des valeurs par défaut
            foreach (var fieldName in selectedFields)
            {
                record[fieldName] = "Valeur par défaut"; // Ici tu peux ajouter des logiques pour définir les valeurs par défaut par champ
            }

            // Créer l'enregistrement dans Dataverse
            return _serviceClient.Create(record);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Erreur lors de la création de l'enregistrement: {ex.Message}");
            return Guid.Empty;
        }
    }

    public Guid CreateRecord(string entityName, Dictionary<string, string> selectedFields)
    {
        try
        {
            // Créer une instance de l'entité
            Entity record = new Entity(entityName);

            // Parcours des champs sélectionnés et ajout des valeurs à l'entité
            foreach (var field in selectedFields)
            {
                // Si l'attribut est de type Picklist (OptionSet)
                if (int.TryParse(field.Value, out int picklistValue))
                {
                    record[field.Key] = new OptionSetValue(picklistValue);
                }
                // Si l'attribut est de type Boolean
                else if (bool.TryParse(field.Value, out bool boolValue))
                {
                    record[field.Key] = boolValue;
                }
                else
                {
                    // Sinon, c'est un texte ou une autre valeur (valeur par défaut)
                    record[field.Key] = field.Value ?? "Valeur par défaut";
                }
            }

            // Créer l'enregistrement dans Dataverse
            return _serviceClient.Create(record);
        }
        catch (Exception ex)
        {
            // Afficher l'exception complète pour le débogage
            Debug.WriteLine($"Erreur lors de la création de l'enregistrement: {ex.Message}");
            Debug.WriteLine($"Détails : {ex.StackTrace}");
            return Guid.Empty;
        }
    }



}




