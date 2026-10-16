import { GITHUB_URL, NUGET_URL } from '../../app/links'
import { navigate } from '../../app/navigation'
import { Brand } from './Brand'

export function Footer() {
  return (
    <footer>
      <div className="section-shell footer-inner">
        <div><Brand /><p>Deterministic event-driven processes for .NET.</p></div>
        <div className="footer-links">
          <button type="button" onClick={() => navigate('docs')}>Documentation</button>
          <a href={GITHUB_URL} target="_blank" rel="noreferrer">GitHub</a>
          <a href={NUGET_URL} target="_blank" rel="noreferrer">NuGet</a>
        </div>
        <p className="footer-note">Early alpha · MIT licensed</p>
      </div>
    </footer>
  )
}
