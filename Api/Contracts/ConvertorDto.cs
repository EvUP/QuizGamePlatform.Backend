using testBdControllers.Core.Abstractions;

namespace testBdControllers.Api.Contracts
{
    public class ConvertorRequest
    {
        public CurrencyCode Type { get; set; }

        public decimal Value { get; set; }
    }

    public class ConvertorResponse: ConvertorRequest
    {
        public decimal Result { get; set; }
    }
}
