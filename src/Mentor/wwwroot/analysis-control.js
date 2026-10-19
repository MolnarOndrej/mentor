export function analysisControl(state, day) {
  const attempts = state?.analyses.filter((a) => a.day === day) || [];
  const active = attempts.find((a) => ['queued', 'running'].includes(a.status));
  if (active)
    return {
      enabled: false,
      status: active.status,
      label: active.status === 'queued' ? 'Ve frontě' : 'Probíhá rozbor',
    };

  const coverage = (state?.conversations || [])
    .filter((c) => c.status === 'ready')
    .flatMap((c) => c.days.filter((d) => d.day === day));
  const messages = coverage.reduce((sum, d) => sum + d.messageCount, 0);
  if (!messages) return { enabled: false, status: 'empty', label: 'Nejsou zprávy k rozboru' };
  return {
    enabled: true,
    status: 'ready',
    label: 'Analyzovat',
  };
}
