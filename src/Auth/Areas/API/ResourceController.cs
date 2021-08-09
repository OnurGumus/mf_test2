using Microsoft.AspNetCore.Mvc;

[ApiController]
[Area("api")]
[Route("[area]/[controller]")]
public class ResourceController : ControllerBase
{
    [HttpGet()]
    public ActionResult<string> GetUser()
    {
        var userName = HttpContext.User?.Identity?.Name;
        if (userName != null)
        {
            return userName;
        }
        return NotFound();
    }
}