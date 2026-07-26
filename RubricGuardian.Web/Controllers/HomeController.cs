using Microsoft.AspNetCore.Mvc;

namespace RubricGuardian.Web.Controllers;

public class HomeController : Controller
{
    [Route("/Home/Error")]
    public IActionResult Error() => View();
}
