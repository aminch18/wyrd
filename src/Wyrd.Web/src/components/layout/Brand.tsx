import type { CSSProperties } from 'react'
import { navigate } from '../../app/navigation'

const logoStyle = (fileName: string) => ({
  backgroundImage: `url(${import.meta.env.BASE_URL}${fileName})`,
}) satisfies CSSProperties

export function Brand() {
  return (
    <button className="brand" type="button" onClick={() => navigate('home')} aria-label="Wyrd home">
      <span className="brand-image brand-image-dark" style={logoStyle('wyrd-logo-dark.png')} aria-hidden="true" />
      <span className="brand-image brand-image-light" style={logoStyle('wyrd-logo-light.png')} aria-hidden="true" />
    </button>
  )
}
