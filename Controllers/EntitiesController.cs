using Microsoft.AspNetCore.Mvc;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using System;
using System.Collections.Generic;

public class EntitiesController : Controller
{
    private readonly DataverseService _dataverseService;

    public EntitiesController(DataverseService dataverseService)
    {
        _dataverseService = dataverseService ?? throw new ArgumentNullException(nameof(dataverseService));
    }

    public IActionResult Index(string solname)
    {
        var entities = _dataverseService.GetAllEntities(solname);
        return View(entities);
    }




    // Afficher les champs disponibles de l'entite
    //


    // Générer le code basé sur les champs sélectionnés


    //
    //public IActionResult SelectAttributes(string entityName)
    //{
    //    var attributes = _dataverseService.GetEntityAttributes(entityName);
    // ViewBag.EntityName = entityName;
    // return View(attributes);
    //}

    // Action pour afficher les champs de l'entité avec des cases à cocher
    [HttpGet("Entities/Details")]
    public IActionResult SelectEntityFields(string entityName)
    {
        // Récupérer les attributs de l'entité depuis Dataverse
        var attributes = _dataverseService.GetEntityAttributesWithOptions(entityName);

        if (attributes == null || !attributes.Any())
        {
            return NotFound("Aucun attribut trouvé pour cette entité.");
        }

        // Passer les attributs à la vue pour qu'ils soient affichés dans le formulaire
        ViewBag.EntityName = entityName;
        return View("Details", attributes);
    }
    [HttpPost("Entities/create")]
    public IActionResult CreateAndOpenEntity(string entityName, Dictionary<string, string> selectedFields)
    {
        if (string.IsNullOrEmpty(entityName))
        {
            return BadRequest("Le nom de l'entité est requis.");
        }

        if (selectedFields == null || !selectedFields.Any())
        {
            return BadRequest("Aucun champ sélectionné.");
        }

        // Créer l'enregistrement dans Dataverse
        Guid recordId = _dataverseService.CreateRecord(entityName, selectedFields);

        if (recordId == Guid.Empty)
        {
            return StatusCode(500, "Erreur lors de la création de l'enregistrement.");
        }

        // Rediriger vers l'enregistrement dans Dynamics 365
        string baseUrl = "https://orgd9328b9c.crm4.dynamics.com/main.aspx";
        string recordUrl = $"{baseUrl}?etn={entityName}&pagetype=entityrecord&id={recordId}";

        return Redirect(recordUrl);
    }
}



