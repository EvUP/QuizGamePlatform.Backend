using Microsoft.AspNetCore.Mvc;
using testBdControllers.Models;
using testBdControllers.Services;

namespace testBdControllers.Controllers
{

    [ApiController]
    [Route("api/[controller]")]
    public class ConvertorController(IConvertorService convertorService) : ControllerBase
    {
        [HttpPost]
        public async Task<ActionResult<OutConverterDto>> GetCurrency([FromBody] InConvertorDto convertorDto)
        {
            try
            {
                var res = await convertorService.GetRubles(convertorDto);
                return Ok(res);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new
                {
                    success = false,
                    message = ex.Message,
                    code = "UNSUPPORTED_CURRENCY"
                });
            }
        }
    }
}
