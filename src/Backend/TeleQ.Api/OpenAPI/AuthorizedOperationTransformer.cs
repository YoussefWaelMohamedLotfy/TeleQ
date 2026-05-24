using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace TeleQ.Api.OpenAPI;

internal sealed class AuthorizedOperationTransformer(
    IAuthenticationSchemeProvider authenticationSchemeProvider,
    IApiDescriptionGroupCollectionProvider apiDescriptionGroupCollectionProvider
) : IOpenApiDocumentTransformer
{
    public async Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken
    )
    {
        var authenticationSchemes = await authenticationSchemeProvider.GetAllSchemesAsync();

        var securityRequirements = new List<OpenApiSecurityRequirement>();

        if (authenticationSchemes.Any(s => s.Name == JwtBearerDefaults.AuthenticationScheme))
        {
            var bearerRef = new OpenApiSecuritySchemeReference(
                JwtBearerDefaults.AuthenticationScheme,
                document
            );
            securityRequirements.Add(new OpenApiSecurityRequirement { [bearerRef] = [] });
        }

        if (authenticationSchemes.Any(s => s.Name == OpenIdConnectDefaults.AuthenticationScheme))
        {
            var oidcRef = new OpenApiSecuritySchemeReference(
                OpenIdConnectDefaults.AuthenticationScheme,
                document
            );
            securityRequirements.Add(new OpenApiSecurityRequirement { [oidcRef] = [] });
        }

        if (securityRequirements.Count == 0)
            return;

        var authorizedOperations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in apiDescriptionGroupCollectionProvider.ApiDescriptionGroups.Items)
        {
            foreach (var description in group.Items)
            {
                var metadata = description.ActionDescriptor.EndpointMetadata;

                if (
                    metadata.OfType<IAllowAnonymous>().Any()
                    || !metadata.OfType<IAuthorizeData>().Any()
                )
                    continue;

                var path = $"/{(description.RelativePath ?? string.Empty).Trim('/')}";
                var method = (description.HttpMethod ?? "GET").ToUpperInvariant();
                authorizedOperations.Add($"{method}:{path}");
            }
        }

        foreach (var (pathKey, pathItem) in document.Paths)
        {
            foreach (var (operationType, operation) in pathItem.Operations)
            {
                var method = operationType.ToString().ToUpperInvariant();
                var normalizedPath = $"/{pathKey.Trim('/')}";
                if (!authorizedOperations.Contains($"{method}:{normalizedPath}"))
                    continue;

                operation.Security ??= [];
                foreach (var req in securityRequirements)
                    operation.Security.Add(req);
            }
        }
    }
}
