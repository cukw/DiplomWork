using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Gateway.Security;

public sealed class ActionPermissionFilter : IAsyncAuthorizationFilter
{
    private readonly PermissionEvaluator _permissionEvaluator;
    private readonly ILogger<ActionPermissionFilter> _logger;

    public ActionPermissionFilter(
        PermissionEvaluator permissionEvaluator,
        ILogger<ActionPermissionFilter> logger)
    {
        _permissionEvaluator = permissionEvaluator;
        _logger = logger;
    }

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var endpoint = context.HttpContext.GetEndpoint();
        if (endpoint?.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            return;

        var authorizeMetadata = endpoint?.Metadata.GetOrderedMetadata<IAuthorizeData>();
        if (authorizeMetadata is null || authorizeMetadata.Count == 0)
            return;

        var user = context.HttpContext.User;
        if (user?.Identity?.IsAuthenticated != true)
            return;

        var permission = ResolvePermission(context.ActionDescriptor as ControllerActionDescriptor);
        if (string.IsNullOrWhiteSpace(permission))
            return;

        if (await _permissionEvaluator.HasPermissionAsync(user, permission, context.HttpContext.RequestAborted))
            return;

        _logger.LogInformation(
            "Access denied by RBAC. Permission={Permission}, Path={Path}, Method={Method}",
            permission,
            context.HttpContext.Request.Path,
            context.HttpContext.Request.Method);

        context.Result = new ObjectResult(new
        {
            message = "Недостаточно прав для выполнения действия",
            permission
        })
        {
            StatusCode = StatusCodes.Status403Forbidden
        };

        return;
    }

    private static string? ResolvePermission(ControllerActionDescriptor? descriptor)
    {
        if (descriptor is null)
            return null;

        var controller = descriptor.ControllerName?.Trim();
        var action = descriptor.ActionName?.Trim();

        if (string.IsNullOrWhiteSpace(controller) || string.IsNullOrWhiteSpace(action))
            return null;

        return $"{controller.ToLowerInvariant()}.{action.ToLowerInvariant()}";
    }
}
