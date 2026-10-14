using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace MerfitCustomerApi.Api.Filters;

/// <summary>
/// MVC'nin form govdesini model binding icin kendiliginden okumasini kapatir; form action icinde okunur.
/// Boylece boyut siniri asimi gibi okuma hatalari model state'e (genel ProblemDetails 400) dusmez,
/// ExceptionHandlingMiddleware uzerinden ApiResponse zarfi ve dogru durum koduyla (413) doner.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class DisableFormValueModelBindingAttribute : Attribute, IResourceFilter
{
    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        var factories = context.ValueProviderFactories;
        factories.RemoveType<FormValueProviderFactory>();
        factories.RemoveType<FormFileValueProviderFactory>();
        factories.RemoveType<JQueryFormValueProviderFactory>();
    }

    public void OnResourceExecuted(ResourceExecutedContext context)
    {
    }
}
