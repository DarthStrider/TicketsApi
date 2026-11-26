using System.Globalization;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Localization;

namespace TicketsApi.Localization;

// T-004: English (default) and French error messages, chosen only from the Accept-Language header.
public static class LocalizationSetup
{
    // Languages for message text (CurrentUICulture, used by IStringLocalizer).
    private static readonly CultureInfo[] MessageLanguages =
        [CultureInfo.GetCultureInfo("en"), CultureInfo.GetCultureInfo("fr")];

    // Formatting and comparison (CurrentCulture) always stay English, whatever the header says, so number,
    // date and string handling can't depend on a client-controlled header (T-004 criteria 13 and 14).
    private static readonly CultureInfo[] FormattingCultures = [CultureInfo.GetCultureInfo("en")];

    public static IServiceCollection AddTicketsLocalization(this IServiceCollection services)
    {
        services.AddLocalization();

        services.Configure<RequestLocalizationOptions>(options =>
        {
            options.DefaultRequestCulture = new RequestCulture("en");
            options.SupportedCultures = FormattingCultures;
            options.SupportedUICultures = MessageLanguages;

            // fr-CA, fr-FR, ... fall back to fr (criterion 2). These are the framework defaults, set explicitly.
            options.FallBackToParentCultures = true;
            options.FallBackToParentUICultures = true;

            // Only the header selects the language: no ?culture= or cookie (criterion 6, T-002 criterion 8).
            options.RequestCultureProviders = [new AcceptLanguageHeaderRequestCultureProvider()];
        });

        // Every ProblemDetails title comes from the resources, whichever code path produced the error
        // (validation, TypedResults.Problem, status-code pages, the exception handler) (criteria 9 and 10).
        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
        {
            int status = context.ProblemDetails.Status ?? context.HttpContext.Response.StatusCode;
            string? key = status switch
            {
                StatusCodes.Status400BadRequest => context.ProblemDetails is HttpValidationProblemDetails
                    ? "Title400Validation"
                    : "Title400",
                StatusCodes.Status401Unauthorized => "Title401", // T-006 criterion 27
                StatusCodes.Status403Forbidden => "Title403",
                StatusCodes.Status404NotFound => "Title404",
                StatusCodes.Status405MethodNotAllowed => "Title405",
                StatusCodes.Status409Conflict => "Title409",
                StatusCodes.Status413PayloadTooLarge => "Title413",
                StatusCodes.Status415UnsupportedMediaType => "Title415",
                StatusCodes.Status500InternalServerError => "Title500",
                _ => null,
            };

            // This replaces any title an endpoint set itself; none does today. Detail is left as is: no error path
            // sets it, and the exception handler never puts exception details in it.
            if (key is not null)
            {
                IStringLocalizer<Messages> localizer = context.HttpContext.RequestServices.GetRequiredService<IStringLocalizer<Messages>>();
                context.ProblemDetails.Title = localizer[key];
            }
        });

        return services;
    }

    // Must run first, before the exception handler and status-code pages, so the errors they write
    // (413, 500, a non-GUID 404) are localized too (criterion 10).
    public static IApplicationBuilder UseTicketsLocalization(this IApplicationBuilder app)
    {
        app.UseRequestLocalization();

        // Content-Language and Vary on every response (criterion 7). Added when the response starts, so they
        // survive the exception handler, which clears the headers before writing its ProblemDetails.
        app.Use((context, next) =>
        {
            string language = CultureInfo.CurrentUICulture.Name;
            context.Response.OnStarting(() =>
            {
                context.Response.Headers.ContentLanguage = language;
                context.Response.Headers.Append("Vary", "Accept-Language");
                return Task.CompletedTask;
            });

            return next(context);
        });

        return app;
    }
}
