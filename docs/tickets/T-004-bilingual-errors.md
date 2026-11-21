# T-004 Bilingual error messages (FR/EN)

## Acceptance criteria
- Validation and error messages are returned in French when the request has `Accept-Language: fr` (or `fr-CA`), otherwise in English.
- Use ASP.NET Core localization (`IStringLocalizer`) with `.resx` resources. No hard-coded strings in endpoints.
- Tests cover both languages for at least one validation error.
