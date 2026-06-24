namespace SvnHub.Web.Support;

public static class PaginationOptions
{
    public static readonly int[] PageSizes = [10, 15, 25, 50, 100];

    public static int NormalizePageSize(int pageSize) =>
        PageSizes.Contains(pageSize) ? pageSize : PageSizes[0];
}
