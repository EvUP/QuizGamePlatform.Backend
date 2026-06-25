using Microsoft.AspNetCore.Mvc;
using testBdControllers.Api.Contracts;
using testBdControllers.Core.Abstractions;

namespace testBdControllers.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class UsersController(IUserService userService) : ControllerBase
    {
        [HttpPost]
        public async Task<ActionResult<List<UserDto>>> Get([FromBody] GetUserDto getUserDto, CancellationToken ct)
        {
            var users = await userService.GetAllUsersAsync(getUserDto.Limit, ct);
            return Ok(users);
        }

        [HttpPost("create")]
        public async Task<ActionResult<UserDto>> Create([FromBody] CreateUserDto dto, CancellationToken ct)
        {
            var user = await userService.AddUserAsync(dto, ct);
            return CreatedAtAction(nameof(Get), new { }, user);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(string id, CancellationToken ct)
        {
            var removed = await userService.RemoveUserAsync(id, ct);

            if (!removed)
            {
                return NotFound(new { message = $"User with id {id} not found." });
            }

            return Ok(new { message = $"User {id} was deleted" });
        }

        [HttpPatch("{id}")]
        public async Task<IActionResult> Update(string id, [FromBody] UpdateUserDto dto, CancellationToken ct)
        {
            var result = await userService.UpdateUserAsync(id, dto, ct);

            if (result == null)
                return NotFound($"User with id {id} not found");

            return Ok(result);
        }
    }


}