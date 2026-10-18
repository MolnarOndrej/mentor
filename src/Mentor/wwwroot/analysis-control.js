export function analysisControl(state, day) {
  const attempts = state?.analyses.filter((a) => a.day === day) || [];
  const active = attempts.find((a) => ['queued', 'running'].includes(a.status));
  if (active)
    return {
      enabled: false,
      status: active.status,
      label: active.status === 'queued' ? 'Ve frontě' : 'Probíhá rozbor',
    };

  const coverage = (state?.conversations || []).flatMap((c) => c.days.filter((d) => d.day === day));
  const messages = coverage.reduce((sum, d) => sum + d.messageCount, 0);
  const analyzed = coverage.reduce((sum, d) => sum + d.analyzedCount, 0);
  if (!messages) return { enabled: false, status: 'empty', label: 'Nejsou zprávy k rozboru' };
  if (analyzed >= messages && day !== state.today)
    return { enabled: false, status: 'completed', label: 'Rozbor je aktuální' };
  return {
    enabled: true,
    status: 'ready',
    label: attempts.some((a) => a.status === 'completed' && a.messageCount > 0)
      ? 'Aktualizovat'
      : 'Analyzovat',
  };
}
