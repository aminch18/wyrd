import source from '../../../../../docs/guides/philosophy.md?raw'
import { MarkdownArticle } from './MarkdownArticle'

export function OverviewDoc() {
    return (
        <MarkdownArticle
            source={source}
            eyebrow="Why Wyrd exists"
            title="The Wyrd philosophy"
            lead="Make business decisions explicit, keep effects at the boundary, and turn a process into a model people can reason about."
        />
    )
}
