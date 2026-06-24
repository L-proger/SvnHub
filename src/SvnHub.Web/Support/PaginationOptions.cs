namespace SvnHub.Web.Support;

public static class PaginationOptions
{
    private const string PageSizeCookieName = "svnhub.pageSize";

    public static readonly int[] PageSizes = [10, 15, 25, 50, 100];

    public static int NormalizePageSize(int pageSize) =>
        PageSizes.Contains(pageSize) ? pageSize : PageSizes[0];

    public static int ResolvePageSize(HttpRequest request, HttpResponse response, int? requestedPageSize)
    {
        if (requestedPageSize is int requested)
        {
            var normalized = NormalizePageSize(requested);
            if (normalized == requested)
            {
                response.Cookies.Append(PageSizeCookieName, normalized.ToString(), BuildCookieOptions(request));
            }

            return normalized;
        }

        if (request.Cookies.TryGetValue(PageSizeCookieName, out var saved) &&
            int.TryParse(saved, out var savedPageSize) &&
            PageSizes.Contains(savedPageSize))
        {
            return savedPageSize;
        }

        return PageSizes[0];
    }

    private static CookieOptions BuildCookieOptions(HttpRequest request) =>
        new()
        {
            Expires = DateTimeOffset.UtcNow.AddYears(1),
            HttpOnly = true,
            IsEssential = true,
            SameSite = SameSiteMode.Lax,
            Secure = request.IsHttps,
        };
}
