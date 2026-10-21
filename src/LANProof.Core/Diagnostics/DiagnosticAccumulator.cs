using LANProof.Core.Models;

namespace LANProof.Core.Diagnostics;

public sealed record DiagnosticHypothesis(NetworkLayer Layer, string Summary, string Advice, int Confidence);

/// <summary>Rolling sufficient statistics. Retains one compact record per cycle, not raw probe payloads.</summary>
public sealed class DiagnosticAccumulator(ProbeConfiguration? configuration = null)
{
    private readonly ProbeConfiguration _config = configuration ?? new();
    private sealed record Cycle(string Device, int Total, int Unknown, int Success, int Partial, int Degraded,
        int[] Counts, int[] Failures, double? GatewayLoss, double? ExternalLoss, DiagnosticHypothesis? Hypothesis);
    private readonly SortedDictionary<(DateTime, long), Cycle> _cycles = [];
    private readonly Dictionary<string, int> _devices = [];
    private readonly Dictionary<DiagnosticHypothesis, int> _hypotheses = [];
    private readonly int[] _counts = new int[5], _failures = new int[5];
    private readonly double[] _lossSum = new double[2];
    private readonly int[] _lossCount = new int[2];
    private int _total, _unknown, _success, _partial, _degraded;
    private long _sequence;
    public int RetainedCycles => _cycles.Count;

    public void AddCycle(IReadOnlyList<ProbeResult> results)
    {
        foreach (var group in results.GroupBy(p => (p.DeviceId, p.Timestamp)))
        {
            var all = group.ToList(); var observed = all.Where(p => p.IsConclusive).ToList();
            var counts = new int[5]; var failures = new int[5];
            foreach (var p in observed) { counts[(int)p.ProbeType]++; if (!p.IsSuccess) failures[(int)p.ProbeType]++; }
            double? Loss(ProbeType type)
            {
                var values = observed.Where(p => p.ProbeType == type && p.PacketLossPercent.HasValue).Select(p => p.PacketLossPercent!.Value).ToList();
                return values.Count > 0 ? values.Average() : null;
            }
            var cycle = new Cycle(group.Key.DeviceId, all.Count, all.Count-observed.Count, observed.Count(p => p.IsSuccess),
                observed.Count(p => p.IsSuccess && p.PacketLossPercent > 0),
                observed.Count(p => p.IsSuccess && !(p.PacketLossPercent > 0) &&
                    (p.ProbeType is ProbeType.Gateway or ProbeType.External && (p.RoundTripMs >= _config.LatencyWarningMs || p.JitterMs >= _config.JitterWarningMs)
                    || p.ProbeType == ProbeType.Dns && p.DnsResolveMs >= _config.LatencyWarningMs
                    || p.ProbeType == ProbeType.Http && p.RoundTripMs >= _config.LatencyWarningMs)),
                counts, failures, Loss(ProbeType.Gateway), Loss(ProbeType.External), Classify(observed));
            _cycles.Add((group.Key.Timestamp, _sequence++), cycle); Apply(cycle, 1);
        }
    }

    public void TrimBefore(DateTime start)
    {
        while (_cycles.Count > 0)
        {
            var first = _cycles.First(); if (first.Key.Item1 >= start) break;
            Apply(first.Value, -1); _cycles.Remove(first.Key);
        }
    }

    private void Apply(Cycle cycle, int sign)
    {
        _total += cycle.Total*sign; _unknown += cycle.Unknown*sign; _success += cycle.Success*sign;
        _partial += cycle.Partial*sign; _degraded += cycle.Degraded*sign;
        _devices[cycle.Device] = _devices.GetValueOrDefault(cycle.Device) + sign;
        if (_devices[cycle.Device] == 0) _devices.Remove(cycle.Device);
        if (cycle.Hypothesis is { } hypothesis)
        {
            _hypotheses[hypothesis] = _hypotheses.GetValueOrDefault(hypothesis) + sign;
            if (_hypotheses[hypothesis] == 0) _hypotheses.Remove(hypothesis);
        }
        for (int i=0; i<5; i++) { _counts[i] += cycle.Counts[i]*sign; _failures[i] += cycle.Failures[i]*sign; }
        foreach (var (index, value) in new[] { (0,cycle.GatewayLoss),(1,cycle.ExternalLoss) })
            if (value.HasValue) { _lossSum[index] += value.Value*sign; _lossCount[index] += sign; }
    }

    public DiagnosticReport BuildReport(DateTime start, DateTime end, IReadOnlyList<ProbeResult>? sample = null,
        IReadOnlyList<NetworkEvent>? events = null, int? totalEvents = null)
    {
        int observed = _total-_unknown, failures = observed-_success;
        var report = new DiagnosticReport
        {
            PeriodStart=start, PeriodEnd=end, TotalMeasurements=_total, InconclusiveMeasurements=_unknown,
            SuccessRatePercent=observed > 0 ? 100d*_success/observed : null,
            GatewayPacketLossPercent=_lossCount[0]>0 ? _lossSum[0]/_lossCount[0] : null,
            ExternalPacketLossPercent=_lossCount[1]>0 ? _lossSum[1]/_lossCount[1] : null,
            IcmpPacketLossPercent=_lossCount.Sum()>0 ? _lossSum.Sum()/_lossCount.Sum() : null,
            SupportingMeasurements=sample?.ToList() ?? [], RelatedEvents=events?.ToList() ?? [],
            MeasurementsAreSampled=(sample?.Count ?? 0) != _total, TotalEvents=totalEvents ?? events?.Count ?? 0,
            HasAnomalies=failures>0 || _partial>0 || _degraded>0 || _unknown>0,
            ProbeSummaries=Enumerable.Range(0,5).Where(i=>_counts[i]>0).Select(i=>new ProbeSummary((ProbeType)i,_counts[i],_failures[i])).ToList()
        };
        if (_total == 0)
        {
            report.Summary="Нет измерений за выбранный период";
            report.Health=HealthLevel.Unknown;
            report.DetailedAnalysis="Запустите мониторинг. Без измерений нельзя оценить соединение."; return report;
        }
        int issues = failures+_partial+_degraded;
        report.Health = observed == 0 ? HealthLevel.Unknown
            : 100d*failures/observed >= 50 ? HealthLevel.Offline
            : 100d*issues/observed >= 60 ? HealthLevel.Unstable
            : issues > 0 ? HealthLevel.Degraded : HealthLevel.Healthy;
        report.DetailedAnalysis=$"Измерений: {_total}; без результата: {_unknown}; неудачных: {failures}; с частичными потерями: {_partial}; с высокой задержкой/джиттером: {_degraded}. Устройств: {_devices.Count}. " +
            "Успешность — доля успешных завершённых проверок. Проверка ICMP может быть успешной при частичных потерях; потери показаны отдельно как среднее по партиям. ";
        var issue = _hypotheses.OrderByDescending(p=>p.Value).ThenBy(p=>p.Key.Summary, StringComparer.Ordinal).FirstOrDefault();
        if (issue.Key is { } chosen)
        {
            report.ProbableLayer=chosen.Layer; report.Summary=chosen.Summary;
            report.ConfidencePercent=Math.Min(chosen.Confidence, issue.Value<3 ? 55 : 80);
            report.DetailedAnalysis+=$"Сочетание признаков найдено в {issue.Value} циклах. Достоверность — эвристическая оценка, не статистическая вероятность. Другие причины не исключены.";
            report.Recommendations.Add(chosen.Advice);
        }
        else if (failures>0 || _partial>0 || _degraded>0 || _unknown>0)
        {
            report.Summary="Есть отклонения; данных для локализации причины недостаточно";
            report.DetailedAnalysis+="Незавершённые проверки не доказывают ни отказа, ни восстановления. Высокая задержка сама по себе не определяет источник.";
            report.Recommendations.Add("Продолжите мониторинг и изучите детали неудачных и незавершённых проверок.");
        }
        else
        {
            report.Summary="Проверки за выбранный период прошли успешно";
            report.DetailedAnalysis+="В эти моменты отказов не обнаружено. Короткие сбои между циклами могли остаться незамеченными.";
            report.Recommendations.Add("Оставьте мониторинг включённым до повторения проблемы.");
        }
        report.Recommendations.Add("Сопоставьте время сбоя с Android-компаньоном. Общий вывод требует измерений обоих устройств и согласованного времени.");
        return report;
    }

    private DiagnosticHypothesis? Classify(IReadOnlyList<ProbeResult> observed)
    {
        var probes=observed.GroupBy(p=>p.ProbeType).ToDictionary(g=>g.Key,g=>g.Last());
        bool Good(ProbeType t)=>probes.GetValueOrDefault(t) is { IsSuccess:true, PacketLossPercent:null or 0 } && !Slow(t) && (t != ProbeType.Dns || !SlowDns());
        bool Bad(ProbeType t)=>probes.GetValueOrDefault(t) is { IsSuccess:false };
        bool Slow(ProbeType t)=>probes.GetValueOrDefault(t) is { IsSuccess:true } p &&
            (p.PacketLossPercent>=_config.PacketLossWarningPercent || p.RoundTripMs>=_config.LatencyWarningMs || p.JitterMs>=_config.JitterWarningMs);
        bool SlowDns()=>probes.GetValueOrDefault(ProbeType.Dns) is { IsSuccess:true, DnsResolveMs:{} value } && value>=_config.LatencyWarningMs;
        double Loss(ProbeType t)=>probes.GetValueOrDefault(t)?.PacketLossPercent ?? 0;
        if (Bad(ProbeType.Adapter)) return new(NetworkLayer.Device,"Не найдено активное подключение со шлюзом","Проверьте кабель, Wi-Fi и состояние сетевого адаптера.",85);
        if (Bad(ProbeType.Gateway)&&Good(ProbeType.Adapter)&&Bad(ProbeType.External)&&Bad(ProbeType.Http)) return new(NetworkLayer.LocalNetwork,"Вероятная проблема между устройством и роутером","Проверьте кабель или Wi-Fi и повторите проверку рядом с роутером. Шлюз также может блокировать ICMP.",70);
        if (Good(ProbeType.Gateway)&&Bad(ProbeType.External)&&Bad(ProbeType.Http)) return new(NetworkLayer.Internet,"Вероятная проблема на выходе в интернет","Проверьте WAN роутера и повторите измерения с другого устройства. Эти данные не доказывают вину провайдера.",75);
        if (Bad(ProbeType.External)&&Good(ProbeType.Http)) return new(NetworkLayer.Internet,"ICMP не отвечает, но HTTPS работает","Внешние узлы или сеть могут ограничивать ICMP. Потерю ping нельзя считать доказанным обрывом интернета.",65);
        if (Bad(ProbeType.Gateway)&&(Good(ProbeType.External)||Good(ProbeType.Http))) return new(NetworkLayer.LocalNetwork,"Шлюз не отвечает на ping, интернет доступен","Проверьте настройки ICMP на роутере. Доступ в интернет в этом цикле сохранялся.",65);
        if (Bad(ProbeType.Dns)&&Good(ProbeType.External)) return new(NetworkLayer.Dns,"Вероятная проблема разрешения имён","Проверьте системный DNS и повторите запросы. Системный кеш может влиять на задержку.",75);
        if (Bad(ProbeType.Http)&&Good(ProbeType.External)&&Good(ProbeType.Dns)) return new(NetworkLayer.Service,"IP и DNS доступны, проверка HTTPS не прошла","Проверьте конкретные адреса, TLS, прокси и состояние сервисов. Измеряется время до HTTP-заголовков, не сетевой RTT.",70);
        if (Slow(ProbeType.Gateway)&&Good(ProbeType.External)&&Good(ProbeType.Http)) return new(NetworkLayer.LocalNetwork,"Отклонения начинаются на участке до роутера","Сравните Wi-Fi и кабель. Ответы ICMP самого роутера могут ограничиваться; при исправных внешних проверках это не доказывает потери пользовательского трафика.",65);
        if (Slow(ProbeType.Gateway)&&Slow(ProbeType.External)&&Loss(ProbeType.Gateway)>Loss(ProbeType.External)) return new(NetworkLayer.LocalNetwork,"Потери до шлюза выше, чем за ним","Роутер может реже отвечать на ICMP, чем пересылать трафик. Сравните с кабелем и другим устройством: эти проценты сами по себе не локализуют потери.",60);
        if (Good(ProbeType.Gateway)&&Slow(ProbeType.External)) return new(NetworkLayer.Internet,"С роутером связь в норме, отклонения начинаются за ним","Сравните с другим устройством этой же сети. Задержка за шлюзом может быть у провайдера или на конкретном маршруте, эти данные их не разделяют.",65);
        if (Good(ProbeType.External)&&SlowDns()) return new(NetworkLayer.Dns,"Разрешение имён медленное при быстром ICMP","Сравните системный DNS с публичным и повторите запросы к разным доменам. Первый запрос включает прогрев кеша.",60);
        if (Good(ProbeType.External)&&Good(ProbeType.Dns)&&Slow(ProbeType.Http)) return new(NetworkLayer.Service,"IP и DNS отвечают быстро, HTTPS отвечает медленно","Проверьте конкретные адреса, прокси и состояние сервиса. Измеряется время до заголовков, оно включает обработку на сервере.",60);
        return null;
    }
}
