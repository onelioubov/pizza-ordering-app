import type { Menu } from '../orderServiceClient';
import type { LineItemForm } from '../useOrderDraft';
import { formatMoney } from '../money';

interface Props {
  menu: Menu;
  index: number;
  item: LineItemForm;
  onSize: (size: string) => void;
  onQuantity: (text: string) => void;
  onToggleTopping: (topping: string) => void;
  onRemove: () => void;
}

export function LineItemEditor({ menu, index, item, onSize, onQuantity, onToggleTopping, onRemove }: Props) {
  const categories = [...new Set(menu.toppings.map((t) => t.category))];

  return (
    <fieldset>
      <legend>Pizza {index + 1}</legend>
      <label>
        Size{' '}
        <select value={item.size} onChange={(e) => onSize(e.target.value)}>
          {menu.sizes.map((s) => (
            <option key={s.id} value={s.id}>
              {s.name} ({formatMoney(s.basePrice)})
            </option>
          ))}
        </select>
      </label>{' '}
      <label>
        Quantity{' '}
        <input type="text" inputMode="numeric" size={3} value={item.quantity} onChange={(e) => onQuantity(e.target.value)} />
      </label>{' '}
      <button type="button" onClick={onRemove}>
        Remove
      </button>
      {categories.map((category) => (
        <div key={category}>
          <strong>{category}:</strong>{' '}
          {menu.toppings
            .filter((t) => t.category === category)
            .map((t) => (
              <label key={t.id} style={{ marginRight: '0.75em' }}>
                <input type="checkbox" checked={item.toppings.includes(t.id)} onChange={() => onToggleTopping(t.id)} />
                {t.name}
              </label>
            ))}
        </div>
      ))}
    </fieldset>
  );
}
