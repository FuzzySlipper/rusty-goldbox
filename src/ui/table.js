import { element, button } from './dom.js';

/**
 * The hosted table as the projection shows it (`view.table`, `view.you`):
 * who plays which party member, who leads, and the open vote. Without a
 * table the game is single-player and every helper says so.
 */

/** The seat of an Engine member, or null. */
export const seatOf = (view, member) => view.table?.seats?.find((seat) => seat.member === member) ?? null;

/** The seat that plays a party member: its owner, else the leader. */
export const ownerOf = (view, index) =>
  view.table?.seats?.find((seat) => seat.characters.includes(index)) ?? seatOf(view, view.table?.leader);

/** Whether this player leads the party (always, when playing alone). */
export const leading = (view) => !view.table || view.table.leader === view.you;

/** Whether this player plays a party member (always, when playing alone). */
export const plays = (view, index) => !view.table || ownerOf(view, index)?.member === view.you;

/** A party member's owner mark: their initial, a dot for whether they are here, a crown when they lead. */
export const ownerMark = (view, index) => {
  const owner = view.table ? ownerOf(view, index) : null;
  if (!owner) {
    return [];
  }

  const leads = owner.member === view.table.leader;
  return [element('span', {
    class: `gb-owner${owner.connected ? '' : ' gb-away'}`,
    title: `${owner.name}${leads ? ' (leads)' : ''}${owner.connected ? '' : ', away'}`,
  }, `${leads ? '♛ ' : ''}${owner.name.slice(0, 1).toUpperCase()}${owner.connected ? ' ●' : ' ○'}`)];
};

/** The names that voted for a menu option. */
export const votersFor = (view, option) => Object.entries(view.table?.votes ?? {})
  .filter(([, chosen]) => chosen === option)
  .map(([member]) => seatOf(view, Number(member))?.name ?? `player ${member}`);

/**
 * The host's invitation as a button that copies it; its text can also be
 * selected by hand. Empty when this player isn't hosting an open game.
 */
export const invitation = (hosting) => hosting?.invitation
  ? [element('p', {}, 'Invitation (click to copy, then send it to your friends):'),
    button(hosting.invitation, () => navigator.clipboard?.writeText(hosting.invitation), { class: 'gb-invitation', title: 'Copy the invitation' })]
  : [];
