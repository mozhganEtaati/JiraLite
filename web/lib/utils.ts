import { clsx, type ClassValue } from "clsx";
import { twMerge } from "tailwind-merge";

/**
 * shadcn's class merger. `cx` in lib/format.ts predates it and only joins —
 * this one also resolves conflicts, so a caller's `px-4` beats a variant's
 * `px-2` instead of both landing in the class list.
 */
export function cn(...inputs: ClassValue[]) {
  return twMerge(clsx(inputs));
}
