/** Centralized environment configuration for the FleetStream frontend. */

export const env = {
  /** BFF API base URL (e.g. http://localhost:8080) */
  apiBaseUrl: process.env.NEXT_PUBLIC_API_BASE_URL ?? "http://localhost:8080",

  /** SignalR hub URL (e.g. ws://localhost:8080/hubs/v1/fleet) */
  signalRHubUrl:
    process.env.NEXT_PUBLIC_SIGNALR_HUB_URL ??
    "http://localhost:8080/hubs/v1/fleet",
} as const;
