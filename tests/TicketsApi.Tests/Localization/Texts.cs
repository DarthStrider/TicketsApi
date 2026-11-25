using System.Net.Http.Headers;
using System.Text;

namespace TicketsApi.Tests.Localization;

// The approved EN/FR text from T-004's "Message text" table, copied here on purpose.
// The tests compare against this table, not against the .resx files the code reads, so a wrong translation
// or a wrong resource fails the tests instead of being compared with itself (T-004 criterion 17).
internal static class Texts
{
    public static readonly IReadOnlyDictionary<string, (string En, string Fr)> All =
        new Dictionary<string, (string En, string Fr)>
        {
            ["TitleRequired"] = ("Title is required.", "Le titre est obligatoire."),
            ["TitleTooLong"] = (
                "Title must be at most 200 characters.",
                "Le titre doit comporter au plus 200 caractères."),
            ["DescriptionTooLong"] = (
                "Description must be at most 4000 characters.",
                "La description doit comporter au plus 4000 caractères."),
            ["StatusRequired"] = ("Status is required.", "Le statut est obligatoire."),
            ["StatusMustBeOneOf"] = (
                "Status must be one of: Open, InProgress, Resolved, InReview, Closed.",
                "Le statut doit être l'une des valeurs suivantes : Open, InProgress, Resolved, InReview, Closed."),
            ["StatusRepeated"] = ("Status must appear only once.", "Le statut ne doit apparaître qu'une seule fois."),
            ["BodyInvalid"] = (
                "The request body must be a valid JSON object.",
                "Le corps de la requête doit être un objet JSON valide."),
            ["Title400Validation"] = (
                "One or more validation errors occurred.",
                "Une ou plusieurs erreurs de validation se sont produites."),
            ["Title400"] = ("Bad Request", "Requête incorrecte"),
            // T-006 criterion 27 (added to T-004's table, wording approved by the product owner).
            ["Title401"] = ("Unauthorized", "Authentification requise"),
            ["Title403"] = ("Forbidden", "Accès refusé"),
            ["Title404"] = ("Not Found", "Ressource introuvable"),
            ["Title409"] = ("Conflict", "Conflit"),
            ["Title413"] = ("Content Too Large", "Contenu trop volumineux"),
            ["Title415"] = ("Unsupported Media Type", "Type de média non pris en charge"),
            ["Title405"] = ("Method Not Allowed", "Méthode non autorisée"),
            ["Title500"] = (
                "An error occurred while processing your request.",
                "Une erreur s'est produite lors du traitement de votre requête."),
        };

    // "fr" means French; anything else (including no header) means English.
    public static string Get(string key, string language) =>
        language == "fr" ? All[key].Fr : All[key].En;

    // Sends a request with an optional Accept-Language header, added without validation so malformed values
    // reach the server as-is (T-004 criterion 5).
    public static Task<HttpResponseMessage> SendAsync(
        this HttpClient client,
        HttpMethod method,
        string url,
        string? acceptLanguage = null,
        string? json = null,
        string contentType = "application/json")
    {
        HttpRequestMessage request = new(method, url);
        if (acceptLanguage is not null)
        {
            request.Headers.TryAddWithoutValidation("Accept-Language", acceptLanguage);
        }

        if (json is not null)
        {
            request.Content = new StringContent(json, Encoding.UTF8);
            request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        }

        return client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    // Maps a test's language parameter to a header: "en" sends no header (the default), "fr" sends "fr".
    public static string? Header(string language) => language == "fr" ? "fr" : null;
}
