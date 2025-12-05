"use client";

import { ProverbsCard } from "@/components/proverbs";
import { WeatherCard, WeatherToolResult, getThemeColor } from "@/components/weather";
import { ContactCard } from "@/components/contact";
import { ContactList } from "@/components/contact-list";
import { MoonCard } from "@/components/moon";
import {
  useCoAgent,
  useFrontendTool,
  useHumanInTheLoop,
  useCopilotAction,
  useDefaultTool 
} from "@copilotkit/react-core";
import { CopilotChat, CopilotKitCSSProperties, CopilotSidebar } from "@copilotkit/react-ui";
import { useState } from "react";
import { AgentState } from "@/lib/types";
import { ContactInfoResult } from "@/components/contact";

export default function CopilotKitPage() {
  const [themeColor, setThemeColor] = useState("#6366f1");
  const [backgroundPattern, setBackgroundPattern] = useState<string | null>(null);

  // 🪁 Frontend Actions: https://docs.copilotkit.ai/microsoft-agent-framework/frontend-actions
  useFrontendTool({
    name: "setThemeColor",
    description: "Set the theme color of the application",
    parameters: [
      {
        name: "themeColor",
        type: "string",
        description: "The theme color to set. Make sure to pick nice colors.",
        required: true,
      },
    ],
    handler: async ({ themeColor }) => {
      setThemeColor(themeColor);
    },
  });

  useFrontendTool({
    name: "setBackgroundPattern",
    description: "Set the background pattern of the application using an SVG string.",
    parameters: [
      {
        name: "svgPattern",
        type: "string",
        description: "The SVG string to use as a background pattern. It should be a valid SVG string.",
        required: true,
      },
    ],
    handler: async ({ svgPattern }) => {
      setBackgroundPattern(svgPattern);
    },
  });

  return (
    <main
      style={
        { "--copilot-kit-primary-color": themeColor } as CopilotKitCSSProperties
      }
    > 
     
        <YourMainContent themeColor={themeColor} backgroundPattern={backgroundPattern} />
      
    </main>
  );
}

function YourMainContent({ themeColor, backgroundPattern }: { themeColor: string, backgroundPattern: string | null }) {
  // 🪁 Shared State: https://docs.copilotkit.ai/pydantic-ai/shared-state
  const { state, setState } = useCoAgent<AgentState>({
    name: "my_agent",
    initialState: {
      proverbs: [
        "CopilotKit may be new, but its the best thing since sliced bread.",
      ],
    },
  }); 

//  useCopilotAction({
//     name: "ListDataverseEnvironments",
//     available: "disabled",
//     render: ({ args, result, status }) => {
//       if (status !== "complete") {
//         return (
//           <div className=" bg-[#667eea] text-white p-4 rounded-lg max-w-md">
//             <span className="animate-spin">⚙️ Retrieving weather!...</span>
//           </div>
//         );
//       }

//       const weatherResult: WeatherToolResult = {
//         temperature: result?.temperature || 0,
//         conditions: result?.conditions || "clear",
//         humidity: result?.humidity || 0,
//         windSpeed: result?.wind_speed || 0,
//         feelsLike: result?.feels_like || result?.temperature || 0,
//       };

//       const themeColor = getThemeColor(weatherResult.conditions);

//       return (
//         <WeatherCard
//           location={args.location}
//           themeColor={themeColor}
//           result={weatherResult}
//         />
//       );
//     },
//   });

useDefaultTool({
  render: ({ name, args, status, result }) => {
    const isComplete = status === "complete";
    const isRunning = status === "inProgress" || status === "executing";

    return (
      <div className="my-3 rounded-xl overflow-hidden shadow-lg bg-gradient-to-br from-slate-900 to-slate-800 border border-slate-700">
        <div className="px-4 py-3 bg-gradient-to-r from-indigo-600 to-purple-600 flex items-center justify-between">
          <div className="flex items-center gap-2">
            <span className="text-lg">⚡</span>
            <h4 className="font-bold text-white tracking-wide">{name}</h4>
          </div>
          <span className={`px-3 py-1 rounded-full text-xs font-semibold ${
            isComplete
              ? "bg-emerald-500 text-white"
              : "bg-amber-500 text-white animate-pulse"
          }`}>
            {isRunning && "Running..."}
            {isComplete && "Complete"}
          </span>
        </div>

        <div className="p-4 space-y-3">
          {Object.keys(args).length > 0 && (
            <div>
              <p className="text-xs font-semibold text-indigo-400 uppercase tracking-wider mb-2">Parameters</p>
              <pre className="text-sm text-slate-200 bg-slate-950/50 p-3 rounded-lg border border-slate-700 overflow-x-auto">
                {JSON.stringify(args, null, 2)}
              </pre>
            </div>
          )}

          {isComplete && result && (
            <div>
              <p className="text-xs font-semibold text-emerald-400 uppercase tracking-wider mb-2">Result</p>
              <pre className="text-sm text-slate-200 bg-slate-950/50 p-3 rounded-lg border border-slate-700 overflow-x-auto max-h-64 overflow-y-auto">
                {JSON.stringify(result, null, 2)}
              </pre>
            </div>
          )}
        </div>
      </div>
    );
  },
});

  useCopilotAction({
    name: "get_contacts",
    available: "disabled",
    parameters: [{ name: "fetchXml", type: "string", required: true }],
    render: ({ args, result, status }) => {
      if (status !== "complete") {
        return (
          <div className=" bg-[#667eea] text-white p-4 rounded-lg max-w-md">
            <span className="animate-spin">⚙️ Fetching from Dataverse!...</span>
          </div>
        );
      }

      if (!result || result.length === 0) {
        return <></>;
      }

      if (result.length === 1) {
        const firstResult = result[0];
        const contactResult: ContactInfoResult = {
          contactid: firstResult?.contactid || "",
          firstname: firstResult?.firstname || "",
          lastname: firstResult?.lastname || "",
          email: firstResult?.email || "",
          mobilephone: firstResult?.mobilephone || "",
        };

        return (
          <ContactCard
            result={contactResult}
            themeColor={themeColor}
          />
        );
      }

      const contactResults: ContactInfoResult[] = result.map((r: any) => ({
        contactid: r?.contactid || "",
        firstname: r?.firstname || "",
        lastname: r?.lastname || "",
        email: r?.email || "",
        mobilephone: r?.mobilephone || "",
      }));

      return <ContactList results={contactResults} themeColor={themeColor} />;
    },
  });

  // 🪁 Human In the Loop: https://docs.copilotkit.ai/microsoft-agent-framework/human-in-the-loop/frontend-tool-based
  useHumanInTheLoop(
    {
      name: "go_to_moon",
      description: "Go to the moon on request.",
      render: ({ respond, status }) => {
        return (
          <MoonCard themeColor={themeColor} status={status} respond={respond} />
        );
      },
    },
    [themeColor],
  );

  const containerStyle = backgroundPattern
    ? {
        backgroundImage: `url("data:image/svg+xml,${encodeURIComponent(backgroundPattern)}")`,
        backgroundColor: themeColor,
        backgroundRepeat: "repeat",
      }
    : { backgroundColor: themeColor };

  return (
    <div
      style={containerStyle}
      className="h-screen w-screen flex justify-center items-center p-16 transition-all duration-300"
    >
      <div className="w-full h-full rounded-2xl overflow-hidden shadow-2xl ring-1 ring-white/20 bg-white/95 backdrop-blur-sm">
         <CopilotChat className="h-full w-full"
        disableSystemMessage={true}
        // clickOutsideToClose={false}
        labels={{
          title: "Assistant",
          initial: "👋 Hi, there! You're chatting with an agent.",
        }}
        // suggestions={[
        //   {
        //     title: "Generative UI",
        //     message: "Get the weather in San Francisco.",
        //   },
        //   {
        //     title: "Frontend Tools",
        //     message: "Set the theme to green.",
        //   },
        //   {
        //     title: "Human In the Loop",
        //     message: "Please go to the moon.",
        //   },
        //   {
        //     title: "Write Agent State",
        //     message: "Add a proverb about AI.",
        //   },
        //   {
        //     title: "Update Agent State",
        //     message:
        //       "Please remove 1 random proverb from the list if there are any.",
        //   },
        //   {
        //     title: "Read Agent State",
        //     message: "What are the proverbs?",
        //   },
        // ]}
      ></CopilotChat>
      </div>
    </div>
  );
}
