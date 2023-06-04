﻿using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace StabilityMatrix.Models.Api;

[JsonConverter(typeof(JsonStringEnumConverter))]
[SuppressMessage("ReSharper", "InconsistentNaming")]
public enum CivitCommercialUse
{
    None,
    Image,
    Rent,
    Sell
}
