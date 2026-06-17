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

        [HttpDelete]
        public async Task<IActionResult> Delete([FromBody] RemoveUserDto removeUserDto)
        {
            var removed = await userService.RemoveUserAsync(removeUserDto.Id);

            if (!removed)
            {
                return NotFound(new { message = $"User with id {removeUserDto.Id} not found." });
            }

            return Ok(new { message = $"User {removeUserDto.Id} was deleted" });
        }
    }
}