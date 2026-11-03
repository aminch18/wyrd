import { useCallback, useEffect, useState } from 'react'

/**
 * Steps 0..last, one every `interval` ms while playing. It never starts on its own:
 * the reader presses play, or steps by hand.
 */
export function useStepper(last: number, interval: number) {
    const [step, setStep] = useState(0)
    const [playing, setPlaying] = useState(false)

    useEffect(() => {
        if (!playing) return
        if (step >= last) {
            setPlaying(false)
            return
        }
        const timer = window.setTimeout(() => setStep((s) => s + 1), interval)
        return () => window.clearTimeout(timer)
    }, [playing, step, last, interval])

    const play = useCallback(() => {
        setStep((s) => (s >= last ? 0 : s))
        setPlaying(true)
    }, [last])
    const pause = useCallback(() => setPlaying(false), [])
    const next = useCallback(() => {
        setPlaying(false)
        setStep((s) => Math.min(last, s + 1))
    }, [last])
    const back = useCallback(() => {
        setPlaying(false)
        setStep((s) => Math.max(0, s - 1))
    }, [])
    const reset = useCallback(() => {
        setPlaying(false)
        setStep(0)
    }, [])

    return { step, setStep, playing, play, pause, next, back, reset }
}
