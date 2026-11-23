# T-004 Bilingual error messages (FR/EN)

**As a** client developer whose users speak French or English, **I want** error messages in my users' language **so that** I can show them to support agents without translating them myself.

## Acceptance criteria

### Language selection
1. The supported languages are English (`en`, the default) and French (`fr`). Given no `Accept-Language` header, when a request fails, then the messages are in English.
2. Given `Accept-Language` set to `fr`, `fr-CA`, `fr-FR` or `FR`, when a request fails, then the messages are in French. Matching ignores case, and any French region falls back to `fr`.
3. Given `Accept-Language` set to `en`, `en-US` or `EN`, when a request fails, then the messages are in English.
4. Given a header with quality values, when a request fails, then the supported language with the highest quality wins. Given `fr;q=0.8, en;q=0.9`, then English. Given `en;q=0.8, fr;q=0.9`, then French. Given `de, fr;q=0.5`, then French (the unsupported `de` is skipped).
   - The framework default is kept: only the first 3 entries of the header are tried (so `de, es, it, fr` gives English). This limit is documented here and is not tested past 3 entries.
5. Given only unsupported languages (`de`, `es-MX`), `*`, an empty header, or a malformed one (`!!!`, `fr;q=abc`), when a request fails, then the messages are in English and the status code is the same as without the header. The header itself never causes a `400`, `406` or `500`.
6. Only the `Accept-Language` header selects the language. Given `?culture=fr`, `?ui-culture=fr` or a `.AspNetCore.Culture` cookie and no French header, when a request fails, then the messages are in English. Unknown query parameters are still ignored (T-002 criterion 8).
7. Given any request, successful or not, when I get the response, then it has `Content-Language: en` or `Content-Language: fr` matching the language used, and `Vary: Accept-Language`.

### Which messages are localized
8. Given a request that fails validation (each `errors` message in the "Message text" table, on POST `/tickets`, GET `/tickets?status=` and PATCH `/tickets/{id}/status`), when it is sent with `Accept-Language: fr`, then every message in `errors` is in French. Without the header, it is in English.
9. Given each error response (`400` with `errors`, `400` without `errors`, `404` for an unknown id, `404` for a non-GUID id, `405` for another verb on `/tickets/{id}/status`, `409`, `413`, `415` and `500`), when it is sent with `Accept-Language: fr`, then the ProblemDetails `title` is in French. Without the header, it is in English.
   - The `500` is tested by replacing `ITicketStore` with one that throws, in the test host.
10. The language applies to every error path, including errors raised by the framework or the middleware (malformed or wrong-type JSON on POST, `413`, `415` on POST, `404` for a non-GUID route, `500`), not only errors returned by the endpoint code.

### What clients parse does not change
11. Localization changes only human-readable text (`errors` message strings and `title`). Given the same failing request in English and in French, then the status code, `type`, `status`, the `errors` keys (`title`, `description`, `status`, `body`), `currentStatus` and `requestedStatus` are identical in both languages.
12. Status names inside messages stay canonical in both languages. For example, the French "must be one of" message lists `Open, InProgress, Resolved, InReview, Closed` untranslated, in `TicketStatus` order.
13. Numbers in messages are plain digits with no thousands separator in both languages ("200", "4000", never "4 000" or "4,000").
14. Successful responses do not depend on the language. Given the same successful request with and without `Accept-Language: fr`, then the bodies are the same (`createdAt` stays ISO 8601, status names are canonical). Status matching (T-002 criterion 11, T-003 criterion 12) does not depend on the language.

### Resources and tests
15. Every message comes from an English and a French `.resx` resource read through `IStringLocalizer`. No message text is hard-coded in the endpoints (checked by the code-reviewer).
16. A test proves that the English and French resource files contain exactly the same keys, with no empty values. A message missing in French fails the tests instead of falling back to English.
17. Each message in the "Message text" table has at least one integration test in each language, and the tests check exactly the text in that table.

### Error format
18. Every error response above, in both languages, keeps the ProblemDetails checks of T-002 criterion 17 and T-003 criterion 27: content type `application/problem+json`, a `status` field equal to the HTTP status code, and no exception details.

## Message text

(wording approved by the product owner)

The "Key" column is the `.resx` resource name. French uses a regular space before ":" (OQLF style), not a narrow no-break space (see criterion 13).

| Key | Where | English | French |
|---|---|---|---|
| TitleRequired | errors.title | Title is required. | Le titre est obligatoire. |
| TitleTooLong | errors.title | Title must be at most 200 characters. | Le titre doit comporter au plus 200 caractères. |
| DescriptionTooLong | errors.description | Description must be at most 4000 characters. | La description doit comporter au plus 4000 caractères. |
| StatusRequired | errors.status | Status is required. | Le statut est obligatoire. |
| StatusMustBeOneOf | errors.status | Status must be one of: Open, InProgress, Resolved, InReview, Closed. | Le statut doit être l'une des valeurs suivantes : Open, InProgress, Resolved, InReview, Closed. |
| StatusRepeated | errors.status | Status must appear only once. | Le statut ne doit apparaître qu'une seule fois. |
| BodyInvalid | errors.body | The request body must be a valid JSON object. | Le corps de la requête doit être un objet JSON valide. |
| Title400Validation | title (400 with errors) | One or more validation errors occurred. | Une ou plusieurs erreurs de validation se sont produites. |
| Title400 | title (400 without errors) | Bad Request | Requête incorrecte |
| Title404 | title | Not Found | Ressource introuvable |
| Title409 | title | Conflict | Conflit |
| Title413 | title | Content Too Large | Contenu trop volumineux |
| Title415 | title | Unsupported Media Type | Type de média non pris en charge |
| Title405 | title | Method Not Allowed | Méthode non autorisée |
| Title500 | title | An error occurred while processing your request. | Une erreur s'est produite lors du traitement de votre requête. |
