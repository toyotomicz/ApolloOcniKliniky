import { Link } from 'react-router'
import { branding } from '../config/branding'

export function BrandMark({ className = 'brand-mark' }: { className?: string }) {
  return (
    <svg className={className} viewBox="0 0 32 32" aria-hidden="true" focusable="false">
      <rect className="brand-mark-bg" width="32" height="32" rx="7" fill="#313336" />
      <path d="M16 6 25 25h-4.2L16 14.6 11.2 25H7z" fill="#fff" />
      <path d="M12.6 21.4h6.8l-1.2-2.6h-4.4z" fill="#c93a32" />
    </svg>
  )
}

export function Brand({ to = '/', inverse = false, subtitle = branding.appSubtitle }: { to?: string; inverse?: boolean; subtitle?: string }) {
  return (
    <Link to={to} className={`brand ${inverse ? 'brand-inverse' : ''}`} aria-label={`${branding.appName} – ${subtitle}, úvodní stránka`}>
      <BrandMark />
      <span className="brand-text">
        <span className="brand-name">{branding.appName}</span>
        <span className="brand-sub">{subtitle}</span>
      </span>
    </Link>
  )
}
