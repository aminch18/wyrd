import styles from '../../styles/site.module.css'
import { useState } from 'react'
import { ArrowRight, Github, Menu, Moon, Sun, X } from 'lucide-react'
import { GITHUB_URL } from '../../app/links'
import { navigate, type Page } from '../../app/navigation'
import type { Theme } from '../../hooks/useTheme'
import { Brand } from './Brand'

interface HeaderProps {
    page: Page
    theme: Theme
    onThemeChange: () => void
}

export function Header({ page, theme, onThemeChange }: HeaderProps) {
    const [open, setOpen] = useState(false)

    const go = (target: Page, section?: string) => {
        navigate(target, section)
        setOpen(false)
    }

    return (
        <header className={styles['site-header']}>
            <div className={styles['nav-shell']}>
                <Brand />
                <button
                    className={styles['menu-button']}
                    type="button"
                    onClick={() => setOpen((value) => !value)}
                    aria-label="Toggle navigation"
                    aria-expanded={open}
                >
                    {open ? <X /> : <Menu />}
                </button>
                <nav
                    className={[
                        styles['nav-links'],
                        open ? styles['nav-links-open'] : '',
                    ].join(' ')}
                    aria-label="Main navigation"
                >
                    <button
                        className={page === 'home' ? styles.active : ''}
                        type="button"
                        onClick={() => go('home')}
                    >
                        Why Wyrd
                    </button>
                    <button
                        className={page === 'docs' ? styles.active : ''}
                        type="button"
                        onClick={() => go('docs')}
                    >
                        Documentation
                    </button>
                    <a href={GITHUB_URL} target="_blank" rel="noreferrer">
                        GitHub <Github size={16} />
                    </a>
                    <button
                        className={styles['theme-toggle']}
                        type="button"
                        onClick={onThemeChange}
                        aria-label={`Use ${theme === 'dark' ? 'light' : 'dark'} theme`}
                    >
                        {theme === 'dark' ? (
                            <Sun size={16} />
                        ) : (
                            <Moon size={16} />
                        )}
                    </button>
                    <button
                        className={styles['nav-cta']}
                        type="button"
                        onClick={() => go('docs', 'quick-start')}
                    >
                        Get started <ArrowRight size={15} />
                    </button>
                </nav>
            </div>
        </header>
    )
}
