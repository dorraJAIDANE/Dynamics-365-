using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Busineso.Controllers
{
    public class SolutionsController : Controller
    {
        private readonly DataverseService _dataverseService;

        public SolutionsController(DataverseService dataverseService)
        {
            _dataverseService = dataverseService ?? throw new ArgumentNullException(nameof(dataverseService));
        }
        public IActionResult Index()
        {
            var solutions = _dataverseService.GetAllSolutions();
            return View(solutions);
        }



        // Affiche les champs disponibles pour une entité donnée
    }
}
