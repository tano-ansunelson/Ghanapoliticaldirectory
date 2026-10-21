using Ghanapoliticaldirectory.Models;

namespace Ghanapoliticaldirectory.Services;

/// <summary>Editorial pages. Placeholders are clearly marked and must be completed before launch.</summary>
public static class StaticPages
{
    public static StaticPageModel? Get(string slug) => slug switch
    {
        "about" => new StaticPageModel("About this platform",
            "Ghana Political Directory is an independent informational platform. It documents political leadership, parties, elections, ministries and administrations. It does not conduct elections, register voters or accept votes.",
            new[]
            {
                ("What it is", "A neutral, historical and searchable record. Presentation never ranks or endorses a party or candidate."),
                ("What it is not", "It is not a government service, a campaign website or an official source of election results. Official results are those published by the Electoral Commission of Ghana."),
                ("Independence", "This platform is independent unless formal government authorisation is stated here.")
            }),
        "contact" => new StaticPageModel("Contact",
            "Questions about the platform or its content.",
            new[] { ("Email", "[contact address to be configured]"), ("Corrections", "To report an error, use the Corrections page so it can be reviewed and logged.") }),
        "data-sources" => new StaticPageModel("Data sources",
            "Every important public fact should link to a reliable source.",
            new[]
            {
                ("Priority sources", "Electoral Commission publications, Parliament of Ghana records, official government announcements and ministry publications."),
                ("What we record", "Source title, URL, publication date, verification status and the date the information was last checked."),
                ("Result states", "Provisional, verified, officially declared, corrected and published. Results are labelled official only after verification against the relevant authority.")
            }),
        "privacy" => new StaticPageModel("Privacy policy",
            "Placeholder for the privacy policy.",
            new[] { ("Status", "[to be written and legally reviewed before launch]") }),
        "terms" => new StaticPageModel("Terms of use",
            "Placeholder for the terms of use.",
            new[] { ("Status", "[to be written and legally reviewed before launch]") }),
        "corrections" => new StaticPageModel("Corrections",
            "We correct errors and keep a record of important changes.",
            new[]
            {
                ("How to report an error", "Tell us which page, which fact, and (if possible) the official source that shows the correct information."),
                ("What happens next", "A reviewer checks the source. Corrected election results and appointments keep a change history and appear in the audit log.")
            }),
        _ => null
    };
}
