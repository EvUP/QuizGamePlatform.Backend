using Microsoft.AspNetCore.Mvc;
using testBdControllers.Api.Contracts;
using testBdControllers.Core.Abstractions;

namespace testBdControllers.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ConvertorController(IConvertorService convertorService) : ControllerBase
    {
        [HttpPost]
        public async Task<ActionResult<ConvertorResponse>> GetCurrency([FromBody] ConvertorRequest convertorDto)
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
