import source from '../../../../../docs/guides/from-ddd-to-wyrd.md?raw'
import { MarkdownArticle } from './MarkdownArticle'

export function FromDddDoc() {
    return (
        <MarkdownArticle
            source={source}
            eyebrow="A change in perspective"
            title="From classic DDD to Wyrd"
            lead="Keep the language, boundaries, and invariants. Express the decisions as pure functions and let the runtime carry them out."
        />
    )
}
