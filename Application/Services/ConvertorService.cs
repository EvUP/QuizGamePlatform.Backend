using testBdControllers.Api.Contracts;
using testBdControllers.Core.Abstractions;
namespace testBdControllers.Application.Services
{
    public class ConvertorService : IConvertorService
    {
        private static readonly Dictionary<CurrencyCode, decimal> _rates = new()
        {
            [CurrencyCode.USD] = 75m,
            [CurrencyCode.THB] = 2.5m,
            [CurrencyCode.TRY] = 2.80m
        };

        public  Task<ConvertorResponse> GetRubles(ConvertorRequest convertorDto)
        {
            ArgumentNullException.ThrowIfNull(convertorDto);

            if (!_rates.TryGetValue(convertorDto.Type, out decimal rate)) {
                throw new ArgumentException($"Неподдерживаемая валюта: {convertorDto.Type}");
            }

            var result = convertorDto.Value * rate;

            return Task.FromResult(new ConvertorResponse
            {
                Result = result,
                Type = convertorDto.Type,
                Value = convertorDto.Value
            });
        }

    }
}
