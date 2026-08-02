import type { NextConfig } from "next";

const API_ORIGIN = process.env.API_ORIGIN ?? "http://localhost:8080";

const nextConfig: NextConfig = {
  // The floating dev badge sits over the bottom-left of every screen.
  devIndicators: false,

  // The API is a separate origin and sends no CORS headers, so the browser
  // never talks to it directly — everything goes same-origin through here.
  async rewrites() {
    return [
      { source: "/api/:path*", destination: `${API_ORIGIN}/api/:path*` },
      { source: "/files/:path*", destination: `${API_ORIGIN}/files/:path*` },
    ];
  },
};

export default nextConfig;
