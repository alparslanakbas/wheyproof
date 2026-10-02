import { describe, expect, it } from 'vitest';

import {
  QUIZ_FOLLOW_UPS,
  SUPPLEMENT_GOALS,
  findSupplementGoal,
  personalNotes,
  proteinTypeFor,
  validAnswers,
} from './supplement-goals';

const goal = (slug: string) => findSupplementGoal(slug)!;

describe('goal configuration', () => {
  // The "Current prices" button of a verdict row scrolls to a list on the same
  // page; a typo in the id would leave the button silently doing nothing.
  it.each(SUPPLEMENT_GOALS.map((g) => [g.slug, g] as const))('%s: verdict links point at existing lists', (_, g) => {
    const ids = new Set(g.picks.map((p) => p.id));
    for (const verdict of g.verdicts) {
      if (verdict.pickSectionId) expect(ids).toContain(verdict.pickSectionId);
    }
  });

  it.each(SUPPLEMENT_GOALS.map((g) => [g.slug, g] as const))('%s: has a priority verdict and FAQs', (_, g) => {
    expect(g.verdicts.some((v) => v.level === 'priority')).toBe(true);
    expect(g.faqs.length).toBeGreaterThan(0);
  });

  it('every branch has two follow-up questions (the progress bar says 3 steps)', () => {
    expect(QUIZ_FOLLOW_UPS.protein).toHaveLength(2);
    expect(QUIZ_FOLLOW_UPS.endurance).toHaveLength(2);
  });
});

describe('validAnswers', () => {
  it("keeps only valid answers from the goal's branch", () => {
    const answers = validAnswers(goal('build-muscle'), {
      protein: 'no',
      dairy: 'made-up',
      duration: 'long',
      utm_source: 'x',
    });
    expect(answers).toEqual({ protein: 'no' });
  });

  it('the endurance branch ignores protein answers', () => {
    expect(validAnswers(goal('endurance'), { protein: 'yes', duration: 'short' })).toEqual({ duration: 'short' });
  });
});

describe('proteinTypeFor', () => {
  it('maps the dairy answer to a list type', () => {
    expect(proteinTypeFor({ dairy: 'low-lactose' })).toBe('isolate');
    expect(proteinTypeFor({ dairy: 'plant' })).toBe('plant');
    expect(proteinTypeFor({ dairy: 'any' })).toBeNull();
    expect(proteinTypeFor({})).toBeNull();
  });
});

describe('personalNotes', () => {
  it('writes nothing without answers', () => {
    expect(personalNotes(goal('build-muscle'), {})).toEqual([]);
  });

  it('puts creatine first for someone who hits their protein from food', () => {
    const notes = personalNotes(goal('build-muscle'), { protein: 'yes' });
    expect(notes).toHaveLength(1);
    expect(notes[0].text).toContain('creatine');
  });

  it('links the calculator for "I don’t know"', () => {
    const notes = personalNotes(goal('lose-fat'), { protein: 'unsure' });
    expect(notes[0].link?.path).toBe('/calculators/protein');
  });

  it('every protein-branch goal has text for yes and no', () => {
    for (const g of SUPPLEMENT_GOALS.filter((x) => x.quizBranch === 'protein')) {
      for (const protein of ['yes', 'no']) {
        expect(personalNotes(g, { protein })[0]?.text.length ?? 0).toBeGreaterThan(0);
      }
    }
  });

  it('says short sessions need no gels and heavy sweaters need electrolytes', () => {
    const notes = personalNotes(goal('endurance'), { duration: 'short', sweat: 'yes' }).map((n) => n.text);
    expect(notes[0]).toContain('water is enough');
    expect(notes[1]).toContain('electrolytes');
  });

  it('says the dairy preference narrowed the list', () => {
    const notes = personalNotes(goal('gain-weight'), { protein: 'no', dairy: 'plant' }).map((n) => n.text);
    expect(notes).toHaveLength(2);
    expect(notes[1]).toContain('plant-based');
  });
});
