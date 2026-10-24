import { ArrowRight } from 'lucide-react'
import { routePath } from '../app/navigation'
import styles from '../styles/site.module.css'

export function PhilosophySection() {
    return (
        <section className={styles['philosophy-section']}>
            <div
                className={`${styles['section-shell']} ${styles['philosophy-grid']}`}
            >
                <div className={styles['section-intro']}>
                    <p className={styles['section-number']}>
                        THE CHANGE IN PERSPECTIVE
                    </p>
                    <h2>
                        Keep the domain.
                        <br />
                        <span>Change the mechanics.</span>
                    </h2>
                    <p>
                        DDD starts with understanding the business. Wyrd gives
                        that understanding a more explicit shape in code.
                    </p>
                </div>
                <div className={styles['philosophy-copy']}>
                    <p>
                        An order can be cancelled. A payment can be declined. A
                        reservation can expire. These are the decisions a
                        product team needs to discuss, but their implementation
                        often disappears inside handlers, transactions, and
                        message plumbing.
                    </p>
                    <p>
                        Wyrd separates the rule from its execution: a command
                        and current state produce a fact or a rejection. The
                        state evolves from accepted facts. A process connects
                        those local decisions into a business policy.
                    </p>
                    <p>
                        Your language, aggregate boundaries, and invariants
                        still matter. The shift is that the decision no longer
                        needs to know how a message is delivered or a state
                        change is stored. You can review the rule on its own,
                        test it from facts, and inspect the flow that connects
                        it to the rest of the business.
                    </p>
                    <div className={styles['philosophy-links']}>
                        <a
                            className={styles['text-link']}
                            href={routePath('docs', 'overview')}
                        >
                            Read the philosophy <ArrowRight size={16} />
                        </a>
                        <a
                            className={styles['text-link']}
                            href={routePath('docs', 'from-ddd')}
                        >
                            From classic DDD to Wyrd <ArrowRight size={16} />
                        </a>
                    </div>
                </div>
            </div>
        </section>
    )
}
