"""Claude backend for rwb_agent.py (official Anthropic SDK: pip install anthropic).

Manual tool loop on the Messages API: append-only history (thinking and compaction blocks are passed back
unchanged), server-side compaction for long games, automatic prompt caching, refusal fallbacks."""
import json

MODEL = 'claude-opus-5-5'


def loop(a, log, prompt, tool_desc, tool_schema, run_tool):
    import anthropic
    client = anthropic.Anthropic()
    model = a.model or MODEL
    tools = [{'name': 'rimworld', 'description': tool_desc, 'input_schema': tool_schema}]
    messages = [{'role': 'user', 'content': a.goal}]
    steps = 0
    while steps < a.steps:
        response = client.beta.messages.create(
            model=model,
            max_tokens=16000,
            system=prompt,
            tools=tools,
            messages=messages,
            output_config={'effort': a.effort},
            cache_control={'type': 'ephemeral'},           # caches the growing prefix every turn
            betas=['compact-2026-01-12', 'server-side-fallback-2026-07-01'],
            context_management={'edits': [{'type': 'compact_20260112'}]},
            fallbacks='default',                            # a safety-classifier decline retries on another model
        )
        messages.append({'role': 'assistant', 'content': response.content})
        for b in response.content:
            if b.type == 'text' and b.text: log('say', text=b.text)
        if response.stop_reason == 'refusal':
            log('say', text='[the model declined this turn]'); break
        if response.stop_reason == 'max_tokens':
            messages.append({'role': 'user', 'content': 'Your last message was cut off. Continue with a short message and a tool call.'})
            continue
        calls = [b for b in response.content if b.type == 'tool_use']
        if not calls:
            messages.append({'role': 'user', 'content': 'Continue playing: take a snapshot, decide, act, advance time.'})
            continue
        results = []
        for c in calls:
            inp = c.input if isinstance(c.input, dict) else {}
            out = run_tool(inp.get('cmd', 'snapshot'), inp.get('args'), 'agent:' + model)
            log('tool', cmd=inp.get('cmd'), args=inp.get('args'), result=out)
            results.append({'type': 'tool_result', 'tool_use_id': c.id, 'content': out, 'is_error': '"ok":false' in out[:20]})
            steps += 1
        messages.append({'role': 'user', 'content': results})   # all results of one turn in a single message
