using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Soenneker.QrCodes.Reader.Abstract;

namespace Soenneker.QrCodes.Reader.Registrars;

/// <summary>
/// A utility for reading QR codes.
/// </summary>
public static class QrCodesReaderRegistrar
{
    /// <summary>
    /// Adds <see cref="IQrCodesReader"/> as a singleton service. <para/>
    /// </summary>
    public static IServiceCollection AddQrCodesReaderAsSingleton(this IServiceCollection services)
    {
        services.TryAddSingleton<IQrCodesReader, QrCodesReader>();

        return services;
    }

    /// <summary>
    /// Adds <see cref="IQrCodesReader"/> as a scoped service. <para/>
    /// </summary>
    public static IServiceCollection AddQrCodesReaderAsScoped(this IServiceCollection services)
    {
        services.TryAddScoped<IQrCodesReader, QrCodesReader>();

        return services;
    }
}
