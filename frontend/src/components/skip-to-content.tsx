"use client";

/**
 * Skip-to-content link for keyboard and screen reader accessibility.
 * Renders a visually hidden link that becomes visible on focus.
 * Place at the top of the body/layout, before the main content.
 */

export function SkipToContent() {
  return (
    <a
      href="#main-content"
      className="fixed left-4 top-4 z-[100] -translate-y-full rounded-md bg-blue-600 px-4 py-2 text-sm font-medium text-white shadow-lg transition-transform focus:translate-y-0 focus:outline-none focus:ring-2 focus:ring-blue-400 focus:ring-offset-2"
    >
      Skip to main content
    </a>
  );
}
