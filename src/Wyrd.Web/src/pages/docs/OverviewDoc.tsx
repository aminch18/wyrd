import source from '../../../../../docs/guides/philosophy.md?raw'
import { Callout } from '../../components/common/Callout'
import { MarkdownArticle } from './MarkdownArticle'

export function OverviewDoc() {
    return (
        <MarkdownArticle
            source={source}
            eyebrow="Why Wyrd exists"
            title="The Wyrd philosophy"
            lead="Make business decisions explicit, keep effects at the boundary, and turn a process into a model people can reason about."
            after={
                <Callout title="Go deeper into the design">
                    <a href="#/docs/semantics">How Wyrd behaves</a> lists the
                    rules (messages, slices, processes, delivery), each marked
                    implemented or planned.{' '}
                    <a href="#/docs/decisions">Design decisions</a> explains why
                    each choice was made, what it gives you and what it costs.
                </Callout>
            }
        />
    )
}
