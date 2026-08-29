namespace FleetStream.Core.Domain.Entities;

/// <summary>
/// Represents a delivery truck in the fleet.
/// </summary>
public class Truck
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string LicensePlate { get; set; } = string.Empty;
    public string Status { get; set; } = "Active";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    
    public TruckState? CurrentState { get; set; }
    public ICollection<TruckTelemetry> TelemetryHistory { get; set; } = new List<TruckTelemetry>();
}

/// <summary>
/// Represents the current state of a truck.
/// </summary>
public class TruckState
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string TruckId { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double SpeedKmh { get; set; }
    public double EngineTemperatureCelsius { get; set; }
    public float FuelLevelPercent { get; set; }
    public bool IsMoving { get; set; }
    public bool IsOnline { get; set; }
    public string RiskLevel { get; set; } = "Low";
    public double RiskScore { get; set; }
    public double TotalDistanceKm { get; set; }
    public int ViolationsCount { get; set; }
    public int AnomaliesCount { get; set; }
}

/// <summary>
/// Represents a telemetry event from a truck.
/// </summary>
public class TruckTelemetry
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string TruckId { get; set; } = string.Empty;
    public DateTime EventTimestamp { get; set; }
    public DateTime ProcessedAt { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double SpeedKmh { get; set; }
    public double EngineTemperatureCelsius { get; set; }
    public float FuelLevelPercent { get; set; }
    public string? CountryCode { get; set; }
    public string? Region { get; set; }
    public string? City { get; set; }
    public string? Geohash { get; set; }
    public bool SpeedViolation { get; set; }
    public bool TempAnomaly { get; set; }
    public bool FuelLow { get; set; }
    public bool GeofenceViolation { get; set; }
    public string RiskLevel { get; set; } = "Low";
    public double RiskScore { get; set; }
}

/// <summary>
/// Represents an alert generated from telemetry processing.
/// </summary>
public class Alert
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string TruckId { get; set; } = string.Empty;
    public string AlertType { get; set; } = string.Empty;
    public string Severity { get; set; } = "Info";
    public string Message { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public bool IsAcknowledged { get; set; }
    public string? AcknowledgedBy { get; set; }
    public DateTime? AcknowledgedAt { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}

/// <summary>
/// Represents a geofence zone.
/// </summary>
public class Geofence
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "Polygon";
    public List<GeofencePoint> Points { get; set; } = new();
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double RadiusKm { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Represents a point in a geofence.
/// </summary>
public class GeofencePoint
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
}
