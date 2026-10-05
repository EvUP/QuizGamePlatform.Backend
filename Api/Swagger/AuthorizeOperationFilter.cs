using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi.Models;
using QuizGamePlatform.Backend.Application.Auth;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace QuizGamePlatform.Backend.Api.Swagger
{
    public class AuthorizeOperationFilter : IOperationFilter
    {
        public void Apply(OpenApiOperation operation, OperationFilterContext context)
        {
            var metadata = context.ApiDescription.ActionDescriptor.EndpointMetadata;

            var optional = metadata.OfType<OptionalAuthorizationAttribute>().Any();
            var required = !metadata.OfType<IAllowAnonymous>().Any()
                && metadata.OfType<IAuthorizeData>().Any();

            if (!required && !optional)
            {
                return;
            }

            if (optional && !required)
            {
                operation.Security.Add(new OpenApiSecurityRequirement());
            }

            foreach (var schemeId in new[] { "Keycloak", AuthenticationSchemes.Guest })
            {
                operation.Security.Add(new OpenApiSecurityRequirement
                {
                    {
                        new OpenApiSecurityScheme
                        {
                            Reference = new OpenApiReference
                            {
                                Type = ReferenceType.SecurityScheme,
                                Id = schemeId,
                            },
                        },
                        Array.Empty<string>()
                    },
                });
            }
        }
    }
}
