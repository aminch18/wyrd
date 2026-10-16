import { useEffect, useState } from 'react'
import { navigate, routeFromHash } from '../app/navigation'

export function useHashRoute() {
  const [hash, setHash] = useState(window.location.hash)
  const route = routeFromHash(hash)

  useEffect(() => {
    const onHashChange = () => setHash(window.location.hash)
    window.addEventListener('hashchange', onHashChange)
    if (!window.location.hash) navigate('home')
    return () => window.removeEventListener('hashchange', onHashChange)
  }, [])

  useEffect(() => {
    document.title = route.page === 'docs'
      ? 'Documentation — Wyrd'
      : 'Wyrd — Deterministic Event-Driven Processes for .NET'
  }, [hash, route.page])

  return route
}
