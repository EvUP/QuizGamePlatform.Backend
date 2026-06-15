
using testBdControllers.Api.Contracts;

namespace testBdControllers.Core.Abstractions
{
    public enum CurrencyCode
    {
        USD, // доллары
        THB, // баты
        TRY  // лиры
    }

    public interface IConvertorService
    {
        Task<ConvertorResponse> GetRubles(ConvertorRequest convertorDto);
    }
}
