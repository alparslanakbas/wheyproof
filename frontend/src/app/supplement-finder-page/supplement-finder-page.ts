import { DOCUMENT } from '@angular/common';
import { Component, DestroyRef, ElementRef, OnInit, computed, inject, signal, viewChild } from '@angular/core';
import { Router, RouterLink } from '@angular/router';

import { buildBreadcrumbJsonLd } from '../core/breadcrumb';
import { PageMetaService, upsertJsonLdScript } from '../core/page-meta.service';
import { SITE_NAME } from '../core/site-identity';
import {
  FINDER_PATH,
  GOAL_QUESTION,
  QUIZ_FOLLOW_UPS,
  QuizOption,
  SUPPLEMENT_GOALS,
  SupplementGoal,
  findSupplementGoal,
} from '../core/supplement-goals';
import { PRICE_UNIT } from '../core/unit-price';
import { SiteHeader } from '../site-header/site-header';

interface QuizStep {
  key: string;
  question: string;
  options: QuizOption[];
}

// The questions after the goal branch by goal, but every branch has two, so the
// progress indicator can say "3 steps" from the start.
const STEP_COUNT = 3;

// "Which supplement should I take?" — a step-by-step quiz. The quiz runs
// entirely in the browser; the result is one SSR page per goal (see
// core/supplement-goals.ts). The SSR output carries the first question and
// links to the four goal pages: the page works without JavaScript, and search
// engines find the goal pages from here.
@Component({
  selector: 'app-supplement-finder-page',
  imports: [RouterLink, SiteHeader],
  templateUrl: './supplement-finder-page.html',
})
export class SupplementFinderPage implements OnInit {
  private readonly router = inject(Router);
  private readonly pageMeta = inject(PageMetaService);
  private readonly document = inject(DOCUMENT);
  private readonly destroyRef = inject(DestroyRef);
  private breadcrumbEl: HTMLScriptElement | null = null;

  private readonly questionHeading = viewChild<ElementRef<HTMLElement>>('questionHeading');

  protected readonly finderPath = FINDER_PATH;
  protected readonly goals = SUPPLEMENT_GOALS;
  protected readonly stepCount = STEP_COUNT;
  protected readonly stepIndexes = Array.from({ length: STEP_COUNT }, (_, i) => i);
  protected readonly unitWord = PRICE_UNIT.word;

  private readonly goal = signal<SupplementGoal | null>(null);
  private readonly answers = signal<Record<string, string>>({});
  /** 0 = the goal question. */
  protected readonly stepIndex = signal(0);

  protected readonly step = computed<QuizStep>(() => {
    const index = this.stepIndex();
    const goal = this.goal();
    if (index === 0 || !goal) {
      return {
        key: 'goal',
        question: GOAL_QUESTION,
        options: SUPPLEMENT_GOALS.map((g) => ({ value: g.slug, label: g.label, description: g.description, icon: g.icon })),
      };
    }
    return QUIZ_FOLLOW_UPS[goal.quizBranch][index - 1];
  });

  protected readonly selected = computed(() => {
    const step = this.step();
    return step.key === 'goal' ? (this.goal()?.slug ?? null) : (this.answers()[step.key] ?? null);
  });

  protected readonly isLastStep = computed(() => this.stepIndex() === STEP_COUNT - 1);

  ngOnInit(): void {
    this.pageMeta.set({
      title: `Which Supplement Should I Take? 3-Question Quiz | ${SITE_NAME}`,
      description:
        'Find out in three questions which supplements actually help with your goal: muscle, weight gain, fat loss or endurance. What to skip, and the lowest prices today.',
      canonicalPath: FINDER_PATH,
    });
    this.breadcrumbEl = upsertJsonLdScript(
      this.document,
      this.breadcrumbEl,
      buildBreadcrumbJsonLd(this.document, [
        { name: 'Home', path: '/' },
        { name: 'Which supplement?', path: FINDER_PATH },
      ]),
    );
    this.destroyRef.onDestroy(() => this.breadcrumbEl?.remove());
  }

  protected choose(value: string): void {
    const step = this.step();
    if (step.key === 'goal') {
      const goal = findSupplementGoal(value) ?? null;
      // A different goal may mean a different branch; earlier answers would belong to the other one.
      if (goal?.slug !== this.goal()?.slug) this.answers.set({});
      this.goal.set(goal);
    } else {
      this.answers.update((answers) => ({ ...answers, [step.key]: value }));
    }
  }

  protected next(): void {
    const goal = this.goal();
    if (!this.selected() || !goal) return;

    if (this.isLastStep()) {
      void this.router.navigate([FINDER_PATH, goal.slug], { queryParams: this.answers() });
      return;
    }
    this.stepIndex.update((index) => index + 1);
    this.focusQuestion();
  }

  protected back(): void {
    if (this.stepIndex() === 0) return;
    this.stepIndex.update((index) => index - 1);
    this.focusQuestion();
  }

  // On a step change focus moves to the new question: a screen reader reads it,
  // and a keyboard user lands at the start of the options.
  private focusQuestion(): void {
    setTimeout(() => this.questionHeading()?.nativeElement.focus());
  }
}
