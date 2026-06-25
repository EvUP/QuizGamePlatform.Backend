using System.ComponentModel.DataAnnotations;

namespace testBdControllers.Api.Contracts
{
    public class UserDto
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Surname { get; set; } = string.Empty;
    }

    public class UpdateUserDto
    {
        [Required]
        public string Name { get; set; } = null!;
        [Required]
        public string Surname { get; set; } = null!;
    }


    public class CreateUserDto
    {
        [Required]
        public string Name { get; set; } = string.Empty;

        [Required]
        public string Surname { get; set; } = string.Empty;
    }

    public class RemoveUserDto
    {
        [Required]
        public string Id { get; set; } = string.Empty;
    }

    public class GetUserDto
    {
        public int? Limit { get; set; }
    }

}