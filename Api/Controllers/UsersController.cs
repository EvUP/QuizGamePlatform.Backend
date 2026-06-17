using Microsoft.AspNetCore.Mvc;
using testBdControllers.Api.Contracts;
using testBdControllers.Core.Abstractions;

namespace testBdControllers.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class UsersController(IUserService userService) : ControllerBase
    {
        [HttpGet]
        public async Task<ActionResult<List<UserDto>>> Get()
        {
            var users = await userService.GetAllUsersAsync();
            return Ok(users);
        }

        [HttpPost]
        public async Task<ActionResult<UserDto>> Create([FromBody] CreateUserDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(new { message = "Name and Surname are required." });
            }

            var user = await userService.AddUserAsync(dto);
            return CreatedAtAction(nameof(Get), new { }, user);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(string id)
        {
            var removed = await userService.RemoveUserAsync(id);
            if (!removed)
            {
                return NotFound(new { message = $"User with id {id} not found." });
            }

            return NoContent();
        }
    }
}