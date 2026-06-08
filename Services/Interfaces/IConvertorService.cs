using testBdControllers.Models;

namespace testBdControllers.Services.Interfaces
{
    public enum CurrencyCode
    {
        USD, // доллары
        THB, // баты
        TRY  // лиры
    }

    public interface IConvertorService
    {
        Task<OutConverterDto> GetRubles(InConvertorDto convertorDto);
    }
}
