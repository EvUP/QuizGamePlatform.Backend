using System.Text.Json.Serialization;
using testBdControllers.Services;

namespace testBdControllers.Models
{
    public class InConvertorDto
    {
        public CurrencyCode Type { get; set; }

        public decimal Value { get; set; }
    }

    public class OutConverterDto: InConvertorDto
    {
        public decimal Result { get; set; }
    }
}
