import { computePosition, flip, offset, shift, type Placement } from '@floating-ui/dom';

type Param = string | null | undefined | { text: string | null | undefined; placement?: Placement };

let tip: HTMLDivElement | null = null;
let owner: HTMLElement | null = null;

function element(): HTMLDivElement {
  if (!tip) {
    tip = document.createElement('div');
    tip.className = 'tip';
    tip.setAttribute('role', 'tooltip');
    tip.id = 'reef-tip';
    tip.hidden = true;
    document.body.append(tip);
  }
  return tip;
}

function normalize(param: Param): { text: string; placement: Placement } {
  if (param && typeof param === 'object') return { text: param.text ?? '', placement: param.placement ?? 'top' };
  return { text: param ?? '', placement: 'top' };
}

export function tooltip(node: HTMLElement, param: Param) {
  let current = normalize(param);

  const show = async () => {
    if (!current.text) return;
    const el = element();
    owner = node;
    el.textContent = current.text;
    el.hidden = false;
    node.setAttribute('aria-describedby', el.id);
    const { x, y } = await computePosition(node, el, {
      placement: current.placement,
      middleware: [offset(6), flip(), shift({ padding: 8 })],
    });
    if (owner === node) Object.assign(el.style, { left: `${x}px`, top: `${y}px` });
  };

  const hide = () => {
    if (owner !== node) return;
    owner = null;
    element().hidden = true;
    node.removeAttribute('aria-describedby');
  };

  const events: [string, () => void][] = [
    ['mouseenter', show],
    ['mouseleave', hide],
    ['focusin', show],
    ['focusout', hide],
    ['pointerdown', hide],
  ];
  events.forEach(([name, fn]) => node.addEventListener(name, fn));

  return {
    update(next: Param) {
      current = normalize(next);
      if (owner === node) current.text ? show() : hide();
    },
    destroy() {
      hide();
      events.forEach(([name, fn]) => node.removeEventListener(name, fn));
    },
  };
}
