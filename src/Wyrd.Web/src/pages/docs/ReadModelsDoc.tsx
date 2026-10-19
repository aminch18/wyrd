import source from '../../../../../docs/guides/read-models.md?raw'
import { MarkdownArticle } from './MarkdownArticle'

export function ReadModelsDoc() {
    return (
        <MarkdownArticle
            source={source}
            eyebrow="Guides · read models"
            title="Read models for screens and reports"
            lead="Shape the view around a question, project facts with pure functions, and make ordering assumptions explicit."
        />
    )
}
