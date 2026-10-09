using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Services.Store;
using WinRT.Interop;

namespace WinUINotes.Services;

internal static class ProLicense
{
    public const string ProductId = "WinUINotes.Pro";
    private static StoreContext? context;

    public static void Initialize(nint windowHandle)
    {
        try
        {
            context = StoreContext.GetDefault();
            InitializeWithWindow.Initialize(context, windowHandle);
        }
        catch
        {
            context = null;
        }
    }

    public static async Task<bool> HasProAsync()
    {
        try
        {
            StoreAppLicense license = await GetContext().GetAppLicenseAsync();
            return license.AddOnLicenses.Values.Any(addOn => addOn.InAppOfferToken == ProductId);
        }
        catch
        {
            // Store licensing is unavailable in unpackaged/local development runs.
            return false;
        }
    }

    public static async Task<StorePurchaseResult> PurchaseAsync()
    {
        StoreProductQueryResult products = await GetContext().GetAssociatedStoreProductsAsync(new[] { "Durable" });
        if (products.ExtendedError is not null)
        {
            throw products.ExtendedError;
        }

        StoreProduct? product = products.Products.Values.FirstOrDefault(item => item.InAppOfferToken == ProductId);
        if (product is null)
        {
            throw new InvalidOperationException($"The Store add-on '{ProductId}' isn't available for this app.");
        }

        return await product.RequestPurchaseAsync();
    }

    private static StoreContext GetContext() => context ?? throw new InvalidOperationException("Store licensing hasn't been initialized.");
}
