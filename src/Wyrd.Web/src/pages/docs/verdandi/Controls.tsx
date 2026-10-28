import type { ReactNode } from 'react'
import { Pause, Play, RotateCcw, SkipBack, SkipForward } from 'lucide-react'
import css from './verdandi.module.css'

interface ControlsProps {
    playing: boolean
    atStart: boolean
    atEnd: boolean
    onPlay: () => void
    onPause: () => void
    onBack: () => void
    onNext: () => void
    onReset: () => void
    children?: ReactNode
}

/** Play, step and reset: every animation on the page can be read one step at a time. */
export function Controls({
    playing,
    atStart,
    atEnd,
    onPlay,
    onPause,
    onBack,
    onNext,
    onReset,
    children,
}: ControlsProps) {
    return (
        <div className={css.controls}>
            {playing ? (
                <button type="button" className={css.primary} onClick={onPause}>
                    <Pause size={15} /> Pause
                </button>
            ) : (
                <button type="button" className={css.primary} onClick={onPlay}>
                    <Play size={15} /> {atEnd ? 'Replay' : 'Play'}
                </button>
            )}
            <button
                type="button"
                onClick={onBack}
                disabled={atStart}
                aria-label="Previous step"
            >
                <SkipBack size={15} />
            </button>
            <button
                type="button"
                onClick={onNext}
                disabled={atEnd}
                aria-label="Next step"
            >
                <SkipForward size={15} />
            </button>
            <button
                type="button"
                onClick={onReset}
                disabled={atStart && !playing}
                aria-label="Reset"
            >
                <RotateCcw size={15} />
            </button>
            {children}
        </div>
    )
}
