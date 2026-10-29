import { useEffect, useState } from 'react';
import {
  ApiError,
  getQuote,
  submitOrder,
  updateOrder,
  type Menu,
  type Order,
  type OrderDraft,
  type Quote,
  type TipMode,
} from './orderServiceClient';

const QUOTE_DEBOUNCE_MS = 250;
const WHOLE_NUMBER = /^\d*$/;
const DECIMAL_NUMBER = /^\d*\.?\d*$/;

export interface LineItemForm {
  key: number;
  size: string;
  /** Raw input text; digits only. */
  quantity: string;
  toppings: string[];
}

export interface DraftForm {
  lineItems: LineItemForm[];
  promoCode: string;
  tipMode: TipMode;
  /** Raw input text; digits and one decimal point only. */
  tipValue: string;
}

/**
 * The order being built, and the server's live quote for it (spec §7). Every edit requests a new
 * quote after a 250 ms pause; responses for anything but the latest edit are dropped. When a quote
 * fails, the last good one stays available, `outOfDate` stays true and `error` says why.
 * `submit` sends the quoted draft with the quoted total as `expectedTotal`: as a new order, or, when
 * `editing` is given, as an update replacing that order's draft (spec §6). If it goes unconfirmed,
 * `retry` resends exactly that attempt with the same key until the draft is edited (spec §7).
 */
export function useOrderDraft(menu: Menu, editing?: Order) {
  const [form, setForm] = useState<DraftForm>(() => (editing ? formFrom(editing) : emptyForm(menu)));
  const [quoted, setQuoted] = useState<{ form: DraftForm; quote: Quote } | null>(null);
  const [error, setError] = useState<ApiError | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [submitError, setSubmitError] = useState<ApiError | null>(null);
  // The last send that got no answer; Retry resends it with the same key.
  const [unconfirmed, setUnconfirmed] = useState<{ form: DraftForm; quote: Quote; key: string } | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    const timer = setTimeout(() => {
      const draft = toOrderDraft(form);
      if (!draft) {
        setError(new ApiError('Enter the tip as a number.'));
        return;
      }
      getQuote(draft, controller.signal).then(
        (quote) => {
          if (controller.signal.aborted) return;
          setQuoted({ form, quote });
          setError(null);
        },
        (e: unknown) => {
          if (controller.signal.aborted) return;
          setError(e instanceof ApiError ? e : new ApiError('Something went wrong while pricing your order.'));
        },
      );
    }, QUOTE_DEBOUNCE_MS);
    return () => {
      clearTimeout(timer);
      controller.abort();
    };
  }, [form]);

  const updateLineItem = (key: number, change: Partial<LineItemForm>) =>
    setForm((f) => ({ ...f, lineItems: f.lineItems.map((li) => (li.key === key ? { ...li, ...change } : li)) }));

  const outOfDate = quoted?.form !== form;
  // Editing the draft makes it a new action: the unconfirmed attempt can no longer be retried.
  const canRetry = unconfirmed?.form === form && !submitting;
  const canSubmit = quoted !== null && !outOfDate && error === null && !submitting && !canRetry;

  /** Sends the draft with the total the customer saw. Resolves with the order, or null if it failed. */
  const send = async (attempt: { form: DraftForm; quote: Quote; key: string }): Promise<Order | null> => {
    const draft = toOrderDraft(attempt.form);
    if (!draft) return null;
    setSubmitting(true);
    setSubmitError(null);
    setUnconfirmed(null);
    try {
      return await (editing
        ? updateOrder(editing.id, draft, attempt.quote.total, attempt.key)
        : submitOrder(draft, attempt.quote.total, attempt.key));
    } catch (e) {
      const apiError = e instanceof ApiError ? e : new ApiError('Something went wrong while sending your order.');
      if (apiError.unconfirmed) setUnconfirmed(attempt);
      // Show the server's total so the next submit confirms what the customer now sees (spec §6.1).
      const pricing = apiError.pricing;
      if (pricing) setQuoted((q) => (q?.form === attempt.form ? { form: attempt.form, quote: pricing } : q));
      setSubmitError(apiError);
      return null;
    } finally {
      setSubmitting(false);
    }
  };

  /** Submits the draft as quoted, with a new key (spec §6.2). Resolves with the order, or null (`submitError` says why). */
  const submit = (): Promise<Order | null> =>
    quoted && canSubmit ? send({ ...quoted, key: crypto.randomUUID() }) : Promise.resolve(null);

  /** Resends the unconfirmed attempt with its original key, so it can't apply twice. */
  const retry = (): Promise<Order | null> => (unconfirmed && canRetry ? send(unconfirmed) : Promise.resolve(null));

  return {
    form,
    /** The last successful quote. May be for an earlier draft; check `outOfDate`. */
    quote: quoted?.quote ?? null,
    /** True until a quote for the current draft arrives. */
    outOfDate,
    /** True when the latest quote request failed; `error` holds the messages. */
    failed: error !== null,
    error,
    /** True when the current draft has a fresh quote and nothing is being sent. */
    canSubmit,
    submitting,
    submitError,
    submit,
    /** True when the last send went unconfirmed and the draft hasn't changed since; offer `retry`. */
    canRetry,
    retry,

    addLineItem: () => setForm((f) => ({ ...f, lineItems: [...f.lineItems, newLineItem(menu)] })),
    removeLineItem: (key: number) => setForm((f) => ({ ...f, lineItems: f.lineItems.filter((li) => li.key !== key) })),
    setSize: (key: number, size: string) => updateLineItem(key, { size }),
    setQuantity: (key: number, text: string) => {
      if (WHOLE_NUMBER.test(text)) updateLineItem(key, { quantity: text });
    },
    toggleTopping: (key: number, topping: string) =>
      setForm((f) => ({
        ...f,
        lineItems: f.lineItems.map((li) =>
          li.key !== key
            ? li
            : {
                ...li,
                toppings: li.toppings.includes(topping)
                  ? li.toppings.filter((t) => t !== topping)
                  : [...li.toppings, topping],
              },
        ),
      })),
    setPromoCode: (promoCode: string) => setForm((f) => ({ ...f, promoCode })),
    setTipMode: (tipMode: TipMode) => setForm((f) => ({ ...f, tipMode })),
    setTipValue: (text: string) => {
      if (DECIMAL_NUMBER.test(text)) setForm((f) => ({ ...f, tipValue: text }));
    },
  };
}

let nextKey = 1;

function newLineItem(menu: Menu): LineItemForm {
  return { key: nextKey++, size: menu.sizes[0]?.id ?? '', quantity: '1', toppings: [] };
}

function emptyForm(menu: Menu): DraftForm {
  return { lineItems: [newLineItem(menu)], promoCode: '', tipMode: 'percent', tipValue: '' };
}

/** The stored order's draft as form text, for editing. */
function formFrom(order: Order): DraftForm {
  const { draft } = order;
  return {
    lineItems: draft.lineItems.map((li) => ({
      key: nextKey++,
      size: li.size,
      quantity: String(li.quantity),
      toppings: li.toppings ?? [],
    })),
    promoCode: draft.promoCode ?? '',
    tipMode: draft.tip?.mode ?? 'percent',
    tipValue: draft.tip ? String(draft.tip.value) : '',
  };
}

/**
 * Form text to the API draft. An empty quantity goes as 0 so the server reports it; an empty tip
 * means no tip. Returns null while the tip is a partial entry that isn't a number yet (".").
 */
function toOrderDraft(form: DraftForm): OrderDraft | null {
  const tipValue = Number(form.tipValue);
  const hasTip = form.tipValue.trim() !== '';
  if (hasTip && Number.isNaN(tipValue)) return null;
  return {
    lineItems: form.lineItems.map((li) => ({ size: li.size, quantity: Number(li.quantity || 0), toppings: li.toppings })),
    promoCode: form.promoCode.trim() === '' ? undefined : form.promoCode,
    tip: hasTip ? { mode: form.tipMode, value: tipValue } : undefined,
  };
}
