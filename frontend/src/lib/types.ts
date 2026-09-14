/**
 * FleetStream BFF API — TypeScript types.
 *
 * Wire-format aligned with:
 *   - BffApi/docs/02-api-contract.md §2.3
 *   - BffApi/src/Core/Domain/Entities/Entities.cs
 *
 * JSON is camelCase on the wire (BFF serializes PascalCase → camelCase).
 */

// ─── Pagination ────────────────────────────────────────────────────

export interface CursorPage<T> {
  items: T[];
  nextCursor: string | null;
  pageSize: number;
  hasMore: boolean;
}

// ─── Fleet Summary ────────────────────────────────────────────────

export interface FleetSummary {
  totalTrucks: number;
  onlineTrucks: number;
  movingTrucks: number;
  idleTrucks: number;
  atRiskTrucks: number;
  averageSpeed: number;
  averageFuelLevel: number;
  generatedAt: string;
}

// ─── Truck ────────────────────────────────────────────────────────

export interface Truck {
  id: string;
  name: string;
  licensePlate: string;
  status: TruckStatus;
  createdAt: string;
  updatedAt: string;
}

export type TruckStatus = "Active" | "Maintenance" | "Retired";

// ─── Truck State ──────────────────────────────────────────────────

export interface TruckState {
  truckId: string;
  timestamp: string;
  latitude: number;
  longitude: number;
  speedKmh: number;
  engineTemperatureCelsius: number;
  fuelLevelPercent: number;
  isMoving: boolean;
  isOnline: boolean;
  riskLevel: RiskLevel;
  riskScore: number;
  totalDistanceKm: number;
  violationsCount: number;
  anomaliesCount: number;
}

export type RiskLevel = "Low" | "Medium" | "High" | "Critical";

// ─── Telemetry ────────────────────────────────────────────────────

export interface TruckTelemetry {
  truckId: string;
  eventTimestamp: string;
  processedAt?: string;
  latitude: number;
  longitude: number;
  speedKmh: number;
  engineTemperatureCelsius: number;
  fuelLevelPercent: number;
  countryCode?: string | null;
  region?: string | null;
  city?: string | null;
  geohash?: string | null;
  speedViolation: boolean;
  tempAnomaly: boolean;
  fuelLow: boolean;
  geofenceViolation: boolean;
  riskLevel: RiskLevel;
  riskScore: number;
}

// ─── Alerts ───────────────────────────────────────────────────────

export interface Alert {
  id: string;
  truckId: string;
  alertType: AlertType;
  severity: AlertSeverity;
  message: string;
  timestamp: string;
  isAcknowledged: boolean;
  acknowledgedBy?: string | null;
  acknowledgedAt?: string | null;
  metadata?: Record<string, unknown>;
}

export type AlertType =
  | "SpeedViolation"
  | "TempAnomaly"
  | "FuelLow"
  | "GeofenceViolation"
  | "StaleData"
  | "RouteDeviation";

export type AlertSeverity = "Info" | "Warning" | "Error" | "Critical";

// ─── Auth ─────────────────────────────────────────────────────────

export interface DevTokenRequest {
  subject: string;
  roles?: string[];
}

export interface DevTokenResponse {
  accessToken: string;
  expiresAt: string;
}

// ─── Error (RFC 7807) ─────────────────────────────────────────────

export interface ProblemDetails {
  type?: string;
  title: string;
  status: number;
  detail?: string;
  instance?: string;
  traceId?: string;
  correlationId?: string;
  errors?: ProblemError[];
}

export interface ProblemError {
  pointer: string;
  code: string;
  message: string;
}

