import { clsx, type ClassValue } from 'clsx'
import { twMerge } from 'tailwind-merge'

/** Composição de classes utilitárias, no formato que o shadcn/ui espera. */
export function cn(...inputs: ClassValue[]) {
  return twMerge(clsx(inputs))
}
