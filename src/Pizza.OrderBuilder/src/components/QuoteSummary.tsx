import { useState } from 'react';
import type { ApiError, Quote } from '../orderServiceClient';
import { formatMoney } from '../money';

interface Props {
  quote: Quote | null;
  outOfDate: boolean;
  failed: boolean;
  error: ApiError | null;
}

export function QuoteSummary({ quote, outOfDate, failed, error }: Props) {
  const [describe, setDescribe] = useState(false);

  return (
    <section>
      <h2>
        Your order{' '}
        {quote && (failed ? <em>(out of date)</em> : outOfDate ? <small>(updating…)</small> : null)}
      </h2>

      {error && (
        <ul role="alert" style={{ color: 'crimson' }}>
          {error.messages.map((m, i) => (
            <li key={i}>{m}</li>
          ))}
        </ul>
      )}

      {!quote ? (
        !error && <p>Pricing your order…</p>
      ) : (
        <div style={{ opacity: failed ? 0.5 : 1 }}>
          <table>
            <tbody>
              {quote.lineItems.map((li, i) => (
                <tr key={i}>
                  <td>{li.description}</td>
                  <td>{formatMoney(li.unitPrice)} each</td>
                  <td>{formatMoney(li.lineTotal)}</td>
                </tr>
              ))}
              <Row label="Subtotal" amount={quote.subtotal} />
              <Row label={quote.promo?.applied ? `Discount (${quote.promo.code})` : 'Discount'} amount={quote.discount} />
              <Row label="Tax" amount={quote.tax} />
              <Row label="Tip" amount={quote.tip} />
              <tr>
                <th colSpan={2} style={{ textAlign: 'left' }}>
                  Total
                </th>
                <th>{formatMoney(quote.total)}</th>
              </tr>
            </tbody>
          </table>
          {quote.promo?.message && <p>{quote.promo.message}</p>}

          <button type="button" onClick={() => setDescribe((d) => !d)}>
            {describe ? 'Hide description' : 'Describe order'}
          </button>
          {describe && <pre>{quote.description}</pre>}
        </div>
      )}
    </section>
  );
}

function Row({ label, amount }: { label: string; amount: number }) {
  return (
    <tr>
      <td colSpan={2}>{label}</td>
      <td>{formatMoney(amount)}</td>
    </tr>
  );
}
