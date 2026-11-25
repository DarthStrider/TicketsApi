using System.Collections;
using System.Globalization;
using System.Resources;
using TicketsApi.Features.Tickets;

namespace TicketsApi.Tests.Localization;

// T-004 criterion 16: the English (neutral) and French resources have the same keys and no empty values,
// so a message missing in French fails here instead of silently falling back to English.
public class ResourceFilesTests
{
    private const string BaseName = "TicketsApi.Localization.Messages";

    [Fact]
    public void Resource_files_have_the_same_keys_and_no_empty_values()
    {
        Dictionary<string, string> english = Load(CultureInfo.InvariantCulture);
        Dictionary<string, string> french = Load(CultureInfo.GetCultureInfo("fr"));

        Assert.Equal(english.Keys.Order(), french.Keys.Order());
        Assert.All(english, entry => Assert.False(string.IsNullOrWhiteSpace(entry.Value), $"EN '{entry.Key}' is empty."));
        Assert.All(french, entry => Assert.False(string.IsNullOrWhiteSpace(entry.Value), $"FR '{entry.Key}' is empty."));
    }

    // The resource keys are exactly the 15 keys of T-004's "Message text" table.
    [Fact]
    public void Resource_keys_match_the_ticket_table()
    {
        Dictionary<string, string> english = Load(CultureInfo.InvariantCulture);

        Assert.Equal(Texts.All.Keys.Order(), english.Keys.Order());
    }

    // Each resource matches the approved text, so the .resx files and the ticket can't drift apart.
    [Fact]
    public void Resource_values_match_the_ticket_table()
    {
        Dictionary<string, string> english = Load(CultureInfo.InvariantCulture);
        Dictionary<string, string> french = Load(CultureInfo.GetCultureInfo("fr"));

        Assert.All(Texts.All, entry =>
        {
            Assert.Equal(entry.Value.En, english.GetValueOrDefault(entry.Key));
            Assert.Equal(entry.Value.Fr, french.GetValueOrDefault(entry.Key));
        });
    }

    // The resources hard-code the status list and the limits, so they must follow the code: adding a status or
    // changing a limit without updating both languages fails here (AC12, AC13, code review).
    [Fact]
    public void Resource_values_follow_the_status_names_and_limits_in_the_code()
    {
        string statuses = string.Join(", ", Enum.GetNames<TicketStatus>());
        string titleMax = TicketsEndpoints.TitleMaxLength.ToString(CultureInfo.InvariantCulture);
        string descriptionMax = TicketsEndpoints.DescriptionMaxLength.ToString(CultureInfo.InvariantCulture);

        foreach (CultureInfo culture in new[] { CultureInfo.InvariantCulture, CultureInfo.GetCultureInfo("fr") })
        {
            Dictionary<string, string> resources = Load(culture);
            Assert.Contains(statuses + ".", resources["StatusMustBeOneOf"]);
            Assert.Contains($" {titleMax} ", resources["TitleTooLong"]);
            Assert.Contains($" {descriptionMax} ", resources["DescriptionTooLong"]);
        }
    }

    // Reads only the resources of that exact culture (no fallback to parents), so a missing French key shows up.
    private static Dictionary<string, string> Load(CultureInfo culture)
    {
        ResourceManager manager = new(BaseName, typeof(Program).Assembly);
        ResourceSet? set = manager.GetResourceSet(culture, createIfNotExists: true, tryParents: false);
        Assert.NotNull(set);

        return set.Cast<DictionaryEntry>().ToDictionary(e => (string)e.Key, e => e.Value as string ?? "");
    }
}
