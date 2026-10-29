import { useEffect, useState } from 'react';
import { ApiError, getQuote, type Menu, type OrderDraft, type Quote, type TipMode } from './orderServiceClient';

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
 */
export function useOrderDraft(menu: Menu) {
  const [form, setForm] = useState<DraftForm>(() => ({
    lineItems: [newLineItem(menu)],
    promoCode: '',
    tipMode: 'percent',
    tipValue: '',
  }));
  const [quoted, setQuoted] = useState<{ form: DraftForm; quote: Quote } | null>(null);
  const [error, setError] = useState<ApiError | null>(null);

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

  return {
    form,
    /** The last successful quote. May be for an earlier draft; check `outOfDate`. */
    quote: quoted?.quote ?? null,
    /** True until a quote for the current draft arrives. */
    outOfDate: quoted?.form !== form,
    /** True when the latest quote request failed; `error` holds the messages. */
    failed: error !== null,
    error,

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
