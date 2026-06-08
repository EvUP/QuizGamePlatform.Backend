using testBdControllers.Models;

namespace testBdControllers.Services
{
    public class ConvertorService : IConvertorService
    {

        private static readonly Dictionary<CurrencyCode, decimal> _rates = new()
        {
            [CurrencyCode.USD] = 75m,
            [CurrencyCode.THB] = 2.5m,
            [CurrencyCode.TRY] = 2.80m
        };

        public  Task<OutConverterDto> GetRubles(InConvertorDto convertorDto)
        {
            ArgumentNullException.ThrowIfNull(convertorDto);

            if (!_rates.TryGetValue(convertorDto.Type, out decimal rate)) {
                throw new ArgumentException($"Неподдерживаемая валюта: {convertorDto.Type}");
            }

            var result = convertorDto.Value * rate;

            return Task.FromResult(new OutConverterDto
            {
                Result = result,
                Type = convertorDto.Type,
                Value = convertorDto.Value
            });
        }

    }
}
